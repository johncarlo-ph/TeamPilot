# Infrastructure Module (`TeamPilot.Infrastructure`)

[← Back to README](../README.md)

## Purpose

`TeamPilot.Infrastructure` implements every interface `TeamPilot.Application` declares:
persistence (EF Core / SQL Server), Git operations (LibGit2Sharp), LLM calls (Claude's HTTP
API), and identity (JWT issuance, OpenID Connect validation). It is the only project allowed
to know about concrete third-party libraries — Application never references EF Core,
LibGit2Sharp, or `System.IdentityModel.Tokens.Jwt` directly.

Its role in the architecture is the Adapter layer: it depends on Application (to implement its
interfaces) and Domain (to persist/manipulate entities), and nothing depends on it except the
composition root (`TeamPilot.API`'s `Program.cs`).

## Architectural decisions

**One `TeamPilotDbContext`, Fluent API configuration per entity.** Every entity has its own
`IEntityTypeConfiguration<T>` class under
[`Persistence/Configurations/`](../src/TeamPilot.Infrastructure/Persistence/Configurations) —
no attribute-based configuration on the entities themselves (that would leak EF Core
attributes into the Domain layer).

**Value generation.** All entity IDs are assigned client-side in the Domain layer
(`Entity.Id = Guid.NewGuid()`), never by the database. This required an explicit fix in
`TeamPilotDbContext.OnModelCreating`:

```csharp
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (entityType.FindProperty(nameof(Entity.Id)) is not null)
    {
        modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
    }
}
```

Without this, EF Core's default `ValueGeneratedOnAdd` convention for GUID keys makes it
ambiguous whether an already-non-default key means "this row already exists" or "this is a new
row with a pre-assigned id." In practice this surfaced as child entities added via an
aggregate method (e.g. `Ticket.AssignAgent` adding to the tracked `Assignments` collection,
rather than an explicit repository `AddAsync`) being misclassified as `Modified` instead of
`Added`, producing a 0-row `UPDATE` instead of an `INSERT` and a `DbUpdateConcurrencyException`.
This is a documented, well-known EF Core gotcha for domain models with client-generated keys —
worth knowing before touching any entity configuration in this project.

**Cascade-delete paths.** SQL Server rejects a foreign key whose `ON DELETE CASCADE` would
create *two* converging paths from a common ancestor to the same descendant table. This was
hit twice in this codebase:
- `Conflict → Commit` is `NoAction` (not `Cascade`), because `Ticket` already cascades to both
  `Commit` and `Conflict` directly — a second cascading path via `Commit` would converge.
- `PipelineRun → Ticket` is likewise `NoAction` for the same reason (`Project` cascades to
  both `Ticket` and `PipelineRun` directly).

Any new FK you add between two tables that already share a cascading ancestor needs the same
consideration.

**`User.Roles` as a primitive collection, not a child table.** `Roles` is a small, fixed-size
enum list, mapped via EF Core's primitive-collection support (`builder.PrimitiveCollection(...)`)
to a JSON array column — simpler than a separate `UserRole` join table for what's really just a
scalar-shaped value.

**One generic `ExternalIdentityValidator` for all OIDC providers.** Google and Microsoft are
both pure OpenID Connect providers, so rather than a `GoogleIdTokenValidator` and a
`MicrosoftIdTokenValidator` with duplicated validation logic, there's a single implementation
configured per-provider (`Auth:Providers:{Name}:{MetadataAddress,Audience}`) using
`ConfigurationManager<OpenIdConnectConfiguration>` to fetch each provider's discovery document
and JWKS.

**Symmetric (HS256) JWT signing.** The API only ever validates tokens it issued itself, so a
shared symmetric key (`Jwt:SigningKey`, via user-secrets) is sufficient — there's no need for
asymmetric keys unless a second service needs to validate TeamPilot's tokens independently.

**`ILlmConnector` is provider-pluggable by design, only Claude is implemented.**
`InfrastructureServiceCollectionExtensions.AddLlmConnector` reads `Llm:Provider` and registers
the matching connector; an unsupported value throws `NotSupportedException`. Adding OpenAI or
Mistral later means adding a new `case` and a new connector class — no changes to any caller.

**`ILlmConnector` has two methods: single-shot and multi-turn/tool-use, kept deliberately
separate.** `SendPromptAsync(LlmRequest)` is the original single flat prompt/response shape used
by the fixed pipeline stages (`OrchestrationService`, `ConflictResolutionService`) — untouched by
the addition below. `SendConversationAsync(LlmConversationRequest)` is a second method added for
the Live Agent chat: it carries a `Messages` history, an optional `System` prompt, and optional
`Tools` (JSON-Schema tool definitions), and returns an `LlmConversationResponse` whose `Content`
is a list of typed blocks (`LlmTextBlock`/`LlmToolUseBlock`/`LlmToolResultBlock`) plus a
`StopReason` ("tool_use" vs. "end_turn"). `ClaudeLlmConnector.SendConversationAsync` builds the
real Anthropic Messages API `system`/`messages`/`tools` JSON via `System.Text.Json.Nodes` (block
shapes vary by type, which doesn't fit a single anonymous-object shape) and parses the response
blocks back. Folding this into `LlmRequest`/`SendPromptAsync` instead was deliberately avoided —
it would have meant touching every pipeline call site and every existing test for no benefit to
those single-shot callers.

**`IGitService` takes a `repositoryPath` per call, not a single configured path.** Since each
`Project` owns its own sandbox clone, the service holds no per-project state; every method
(`CloneAsync`, `PushAsync`, `FetchAsync`, `BranchExistsAsync`, `EnsureBranchAsync`,
`CommitFileAsync`, `GetDiffAsync`, `DetectMergeConflictsAsync`, `MergeWithResolutionsAsync`,
`DeleteBranchAsync`, `ListFilesAsync`, `ReadFileAsync`) opens and disposes its own
`LibGit2Sharp.Repository` handle per call (`CloneAsync` is the one exception — it creates the
repository rather than opening an existing one).

**`ListFilesAsync`/`ReadFileAsync` are read-only and exist solely for the Live Agent chat's
sandboxed file tools.** Every other `IGitService` method either writes or talks to the remote;
these two only ever read off `repo.Info.WorkingDirectory`. Both resolve the caller's relative
path against that working directory via `Path.GetFullPath` and reject the call
(`GitOperationException`) if the resolved path doesn't stay under it — the only defense against a
path-traversal attempt (e.g. `../../`) reaching outside the sandbox. `ReadFileAsync` additionally
refuses well-known secret-bearing paths outright (`.git/**`, `.env*`, `id_rsa*`/`id_ed25519*`,
`*.pfx`/`*.pem`/`*.key`/`*.p12`), runs the remaining content through `SecretRedactor`'s
best-effort regex redaction (AWS-style keys, private-key blocks, JWTs, common
`password=`/`api_key=`-shaped assignments), and truncates past 20,000 characters. None of this is
a guarantee against leaking a secret shaped differently than these patterns — it's defense in
depth, not a substitute for keeping real secrets out of a project's repository. `ListFilesAsync`
returns a shallow (non-recursive), capped-at-200-entries listing of one directory, so the model
only sees what it explicitly asked for rather than an implicit full tree.

**`MergeWithResolutionsAsync` finishes a conflicted merge as a real two-parent commit by resuming
LibGit2Sharp's own merge-in-progress state, not by re-merging after the fact.** It runs the same
trial merge `DetectMergeConflictsAsync` does (`repo.Merge(..., CommitOnSuccess: false,
FailOnConflict: false)`), which updates the working tree/index and leaves `MERGE_HEAD` set but
doesn't reset anything. For each `repo.Index.Conflicts` entry it finds, it looks up that file's
path in the caller-supplied `resolutions` map; a hit gets written to disk and `Commands.Stage`d
(clearing that index conflict), a miss gets collected as an unresolved path. Once every
conflicting file is covered, `repo.Commit(...)` is called directly - LibGit2Sharp sees the
still-set `MERGE_HEAD` and records it as a second parent automatically, exactly like completing a
conflicted `git merge` by hand (edit the file, `git add`, `git commit`). If anything is left
unresolved, the attempt is aborted (`repo.Reset(ResetMode.Hard, target.Tip)`, same as
`DetectMergeConflictsAsync`'s cleanup) rather than left mid-merge, since every ticket in a project
shares that project's one sandbox clone and a real other ticket's pipeline stage could touch it
next. `MergeStatus.UpToDate`/`FastForward` short-circuit with nothing to commit (there's no merge
state to finish), and a clean `NonFastForward` merge just needs the one explicit commit call since
`CommitOnSuccess: false` only skips the auto-commit, not the merge itself. See
[docs/application.md](application.md) for how `ApprovalGateService.ApproveAsync` builds the
`resolutions` map from the ticket's `Conflict` records.

**`DeleteBranchAsync` pushes an empty source ref (`:refs/heads/{branchName}`) to delete the
remote branch** — the standard Git protocol convention, supported directly by LibGit2Sharp's
`Network.Push(Remote, string, PushOptions)` overload rather than needing a special "delete" API.
It also guards against deleting the branch that's currently checked out in the sandbox (which
LibGit2Sharp would otherwise reject): if `repo.Head` is sitting on the branch being deleted, it
checks out `baseBranchName` first. The local branch ref is removed after the remote delete
succeeds; if it was never fetched/created locally in the first place, that's a silent no-op
rather than an error.

**Projects are cloned into a server-managed sandbox, not pointed at a path someone else prepared.**
`LibGit2SharpGitService.CloneAsync(projectId, remoteUrl, accessToken, ct)` computes
`{Git:SandboxRoot}/{projectId}` (resolved against `IHostEnvironment.ContentRootPath` when
`SandboxRoot` is relative), clones there with `Repository.Clone`, and returns the resulting
path for `ProjectService` to store as `Project.RepositoryPath`. `PushAsync`/`FetchAsync` open
that same sandbox and talk to its `origin` remote. All three wrap `LibGit2SharpException` as
the Application's `GitOperationException` (→ HTTP 422) instead of letting it bubble to a
generic 500, since a bad URL/token is a client-facing, actionable error.

**Credentials: PAT-over-HTTPS, GitHub/GitLab convention.** `BuildCredentials` builds a
`UsernamePasswordCredentials { Username = accessToken, Password = "" }` for every remote
operation — the convention GitHub and GitLab both accept. Azure DevOps/Bitbucket may need the
token in the password slot instead; this is centralized in one private helper, so supporting
another host is a one-line change there, not a wider refactor.

**Access tokens are encrypted at rest via ASP.NET Core Data Protection, not stored plaintext.**
`DataProtectionGitCredentialProtector` (`Infrastructure/Git/`) wraps
`IDataProtectionProvider.CreateProtector("TeamPilot.Git.AccessToken.v1")`; `ProjectService`
encrypts a submitted token before calling `Project.Create`/`RotateAccessToken`, and decrypts it
just before passing it to `IGitService`. **Caveat:** Data Protection's default key ring is
persisted per-machine — if the API is ever scaled out to multiple instances/containers, the key
ring must be persisted somewhere shared (e.g. a file share or blob store), or a token encrypted
on one instance won't decrypt on another.

## Code style notes

- Repository classes are thin: a query or two per method, `AsNoTracking()` for anything not
  going to be mutated, `.Include()` only where the caller actually needs the related data
  loaded (see `TicketRepository.GetByIdAsync` vs. `ListAsync`).
- `TicketRepository.GetStatusAsync` and `GetByBranchNameAsync` are both deliberately
  `AsNoTracking()` projections/queries rather than reusing `GetByIdAsync` - a second tracking
  query against the same already-tracked `Ticket` would just return the change tracker's
  identity-mapped instance instead of hitting the database, which defeats the point of checking
  for another request's concurrent update (used by `OrchestrationService` to notice a
  mid-pipeline cancellation, and by `TicketService.LinkBranchAsync` to enforce one branch per
  ticket).
- `Tickets` has a composite unique index on `(ProjectId, BranchName)` with a SQL Server filtered
  predicate (`HasFilter("[BranchName] IS NOT NULL")`) - the first filtered index in the schema,
  needed because `BranchName` is nullable for every ticket that hasn't linked one yet and a
  plain unique index would only tolerate a single `NULL` row per project.
- `WorkflowStageConfiguration` maps a project's agent workflow the same "reference by id only"
  way as `Commit`/`Review`/`Conflict` on `Ticket` - no `Project`/`Agent` navigation properties.
  Its self-referencing `LoopBackToStageId` foreign key uses `DeleteBehavior.NoAction` because SQL
  Server rejects a cascading self-reference outright; `WorkflowService` already refuses to remove
  a stage another stage's loop-back targets, so that path is never expected to fire. A unique
  index on `(ProjectId, Order)` backs the one-stage-per-position invariant at the database level.
  The `AddWorkflowStages` migration also backfills every pre-existing project with the same
  Research → Design → Coding → Testing sequence `WorkflowService.EnsureDefaultWorkflowAsync`
  would create for a new one, via a raw `migrationBuilder.Sql(...)` script - see
  [docs/application.md](application.md) for why the pipeline moved from hardcoded stages to this
  table.
- `StageExecutionConfiguration` follows the same "reference by id only" shape, with an
  `AgentId` foreign key matching `TicketAgentAssignment.AgentId` exactly (`DeleteBehavior.Restrict`
  - safe because any agent with a `StageExecution` already has a `TicketAgentAssignment`, which
  `WorkflowService.DeleteCustomAgentAsync` already refuses to delete past). An index on
  `(TicketId, AgentId, CreatedAtUtc)` backs `StageExecutionRepository.GetLatestByTicketAsync`'s
  "most recent output per agent" lookup - computed by grouping in memory after one filtered
  fetch rather than a `GroupBy().Select(g => g.OrderBy...First())` query, since EF Core's SQL
  Server translation of "latest row per group" isn't reliable across versions and a single
  ticket's total execution count is small enough that this isn't a real cost. The
  `AddStageExecutions` migration has no backfill, unlike `AddWorkflowStages` - this is new data
  collected going forward only, with nothing pre-existing to reconstruct.
- `TicketQuestionConfiguration` follows the same shape as `StageExecutionConfiguration` - cascade
  on `TicketId`, restrict on `AgentId` - except `AgentId` is nullable (a failure isn't always tied
  to a specific stage, e.g. the initial branch-link call), an optional relationship EF Core
  handles automatically for a nullable FK. `Kind`/`Status` are stored as strings, same convention
  as `Ticket.Status`/`Review.Decision`. The `AddTicketBlockedStatusAndQuestions` migration only
  adds the new table - `TicketStatus.Blocked` needed no schema change since `Ticket.Status` was
  already a `nvarchar(20)` string column.
- Options classes (`JwtOptions`, `GitOptions`, `LlmOptions`, `ExternalProviderConfig`) are
  plain POCOs with a `public const string SectionName` for their configuration section, bound
  via `services.Configure<T>(configuration.GetSection(T.SectionName))`.
- Stateless services with no `DbContext` dependency (`AccessTokenGenerator`,
  `RefreshTokenGenerator`, `ExternalIdentityValidator`) are registered as **singletons**;
  anything touching `TeamPilotDbContext` is **scoped**.

## Workflow integration

`InfrastructureServiceCollectionExtensions.AddInfrastructure(IServiceCollection, IConfiguration)`
is the single entry point `Program.cs` calls to wire up every repository, the `DbContext`, and
the auth/git/llm services. Migrations are managed with the standard EF Core CLI workflow:

```bash
dotnet ef migrations add <Name> --project src/TeamPilot.Infrastructure --startup-project src/TeamPilot.API
dotnet ef database update --project src/TeamPilot.Infrastructure --startup-project src/TeamPilot.API
```

A `TeamPilotDbContextFactory : IDesignTimeDbContextFactory<TeamPilotDbContext>` exists so
`migrations add` doesn't need the API host to build successfully first — it reads
`appsettings.json`/`appsettings.Development.json` directly.

## Authentication

| Component | File | Responsibility |
|---|---|---|
| `ExternalIdentityValidator` | [`Auth/ExternalIdentityValidator.cs`](../src/TeamPilot.Infrastructure/Auth/ExternalIdentityValidator.cs) | Validates a Google/Microsoft `id_token` against that provider's live JWKS |
| `AccessTokenGenerator` | [`Auth/AccessTokenGenerator.cs`](../src/TeamPilot.Infrastructure/Auth/AccessTokenGenerator.cs) | Issues our JWT: `sub`, `name`, `email`, one `role` claim per role, a space-separated `scope` claim derived from roles |
| `RefreshTokenGenerator` | [`Auth/RefreshTokenGenerator.cs`](../src/TeamPilot.Infrastructure/Auth/RefreshTokenGenerator.cs) | Generates a random opaque refresh-token value and its SHA-256 hash for storage (the raw value is never persisted) |

Role → scope mapping (`AccessTokenGenerator.RoleScopes`):

| Role | Scopes |
|---|---|
| `Admin` | `admin`, `tickets:read`, `tickets:write`, `tickets:approve`, `users:manage`, `projects:manage` |
| `Developer` | `tickets:read`, `tickets:write`, `tickets:approve` |
| `Analyst` | `tickets:read`, `tickets:write` |

User identification uses a **plain-text, unique-indexed `Email`** column (not a hash) — a
deliberate product decision so admins can recognize accounts directly in the `Users` listing.
`Name` is synced from the provider's `name` claim on every login via `User.UpdateName`.

## Example

```csharp
// LibGit2SharpGitService.CommitFileAsync — opens a fresh Repository handle per call
using var repo = OpenRepository(repositoryPath);
var branch = GetOrCreateBranch(repo, branchName);
Commands.Checkout(repo, branch);
File.WriteAllText(Path.Combine(repo.Info.WorkingDirectory, relativeFilePath), fileContent);
Commands.Stage(repo, relativeFilePath);
var commit = repo.Commit(message, signature, signature);
```

## Error handling

Infrastructure mostly lets exceptions propagate rather than translating them:
`DbUpdateException` (constraint violations) currently bubbles up to the API's
`GlobalExceptionHandler` and is mapped generically to HTTP 500. Two places Infrastructure *does*
translate an error: `ExternalIdentityValidator` wraps any token-validation failure as the
Application's own `AuthenticationFailedException` (→ 401), and `LibGit2SharpGitService`'s remote
operations (`CloneAsync`/`PushAsync`/`FetchAsync`/`DeleteBranchAsync`'s remote delete) wrap
`LibGit2SharpException` as `GitOperationException` (→ 422) — local-only Git operations
(commit/diff/detect-conflicts/merge) still let `LibGit2SharpException`/`InvalidOperationException`
bubble to 500/409, since those failures point at a server-side bug rather than bad user input.
`MergeWithResolutionsAsync` finding conflicting files it can't resolve is not one of these cases -
that's an expected, common outcome it reports back via its return value (`GitMergeResolutionResult`),
not an exception; `ApprovalGateService` is the one that turns that into a client-facing
`UnresolvedConflictsException` (→ 409).
A Claude API failure that survives the resilience pipeline below (an `HttpRequestException` or
`TimeoutRejectedException`, or a non-success status via `EnsureSuccessStatusCode`) is now wrapped
by `ClaudeLlmConnector` as the Application's own `LlmOperationException` (→ 422) - the same
"client-facing, actionable error instead of a generic 500" treatment `GitOperationException`
already gets, and for the same reason: an LLM provider outage or rate limit is an external-system
failure, not a server bug. `OrchestrationService` specifically catches this (alongside
`GitOperationException`) to block the ticket rather than let it propagate at all - see
[docs/application.md](application.md); everywhere else that calls `ILlmConnector` (e.g.
`LiveAgentChatService`) still just lets it bubble to `GlobalExceptionHandler`'s 422 mapping.

## Resiliency

Both external-network dependencies — the Claude HTTP client and LibGit2Sharp's remote
operations — retry transient failures instead of failing a pipeline stage on the first blip.

**Claude HTTP client (`AddLlmConnector` in
[`InfrastructureServiceCollectionExtensions`](../src/TeamPilot.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs)).**
`.AddStandardResilienceHandler(...)` (from `Microsoft.Extensions.Http.Resilience`) adds retry
(3 attempts, exponential backoff with jitter), a circuit breaker, and attempt/total timeouts
around every `HttpClient` call `ClaudeLlmConnector` makes. The library's own defaults (a 10s
attempt timeout) are too short for a real completion, so the handler is configured from
`Llm:TimeoutSeconds` (default 60s): `AttemptTimeout` is set to that value directly, the circuit
breaker's `SamplingDuration` to twice that (the minimum the library allows), and
`TotalRequestTimeout` to `AttemptTimeout × (MaxRetryAttempts + 1)`. `ClaudeLlmConnector` itself
sets `HttpClient.Timeout = Timeout.InfiniteTimeSpan` so that fixed client-level timeout can't
race the resilience pipeline's own budget and cut a retry short.

**LibGit2Sharp remote operations (`LibGit2SharpGitService.RemoteOperationResilience`).**
LibGit2Sharp isn't `HttpClient`-based, so this is a plain Polly `ResiliencePipeline` (3 retries,
exponential backoff with jitter) wrapping only the calls that already talk to the remote and
translate `LibGit2SharpException` → `GitOperationException`: `CloneAsync`, `PushAsync`,
`FetchAsync`, and the remote-delete push inside `DeleteBranchAsync`. Local-only operations
(commit/diff/detect-conflicts/merge) are deliberately not wrapped — they aren't subject to
network transience, and blindly retrying a file write/commit risks duplicating side effects
rather than just re-attempting a request. `CloneAsync`'s retried delegate also deletes any
partial clone left on disk by a failed attempt before retrying, so a retry doesn't fail with a
spurious "directory already exists" error that would mask the original one. LibGit2Sharp doesn't
distinguish a transient connectivity blip from a permanent error (bad URL/token) by exception
type, so a permanent failure just takes a few extra attempts (with backoff) before surfacing as
the same `GitOperationException` it would have without this pipeline.

## Future considerations

- **Translate `DbUpdateException` into domain-meaningful errors** (e.g. a unique-constraint
  violation on `User.Email` surfacing as a 409 rather than a generic 500) — not done yet
  because no code path currently allows a client to trigger one that isn't already caught by
  FluentValidation first.
- **Asymmetric JWT signing** (RS256) if a second service ever needs to validate TeamPilot's
  access tokens independently.
- **Multi-provider LLM support**: the `ILlmConnector` seam is ready; only `ClaudeLlmConnector`
  exists.
- **Background job runner** for actually executing `PipelineRun`s, once CI/CD needs to do real
  work instead of just tracking status.
