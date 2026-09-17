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
separate.** `SendPromptAsync(LlmRequest)` is the original single flat prompt/response shape,
still used by `ConflictResolutionService` and by any pipeline stage role that needs no repo
access (e.g. Testing, or a custom admin-added agent). `SendConversationAsync(LlmConversationRequest)`
carries a `Messages` history, an optional `System` prompt, and optional `Tools` (JSON-Schema tool
definitions), and returns an `LlmConversationResponse` whose `Content` is a list of typed blocks
(`LlmTextBlock`/`LlmToolUseBlock`/`LlmToolResultBlock`) plus a `StopReason` ("tool_use" vs.
"end_turn"). Originally added only for the Live Agent chat, it's now also used by the Research/
Design/Coding pipeline stages (via `Application/Llm/ToolLoopRunner` — see
[docs/application.md](application.md#workflow-integration)) so they can read specific files
on demand instead of relying on a size-capped snapshot gathered up front.
`ClaudeLlmConnector.SendConversationAsync` builds the real Anthropic Messages API
`system`/`messages`/`tools` JSON via `System.Text.Json.Nodes` (block shapes vary by type, which
doesn't fit a single anonymous-object shape) and parses the response blocks back. Folding this
into `LlmRequest`/`SendPromptAsync` instead was deliberately avoided — it would have meant
touching every single-shot call site and every existing test for no benefit to those callers.

**`IGitService` takes a `repositoryPath` per call, not a single configured path.** Since each
`Project` owns its own sandbox clone, the service holds no per-project state; every method
(`CloneAsync`, `PushAsync`, `FetchAsync`, `BranchExistsAsync`, `EnsureBranchAsync`,
`CommitFilesAsync`, `GetDiffAsync`, `DetectMergeConflictsAsync`, `MergeWithResolutionsAsync`,
`DeleteBranchAsync`, `ListFilesAsync`, `ReadFileAsync`) opens and disposes its own
`LibGit2Sharp.Repository` handle per call (`CloneAsync` and `RemoteBranchExistsAsync` are the two
exceptions — `CloneAsync` creates the repository rather than opening an existing one, and
`RemoteBranchExistsAsync` never opens a local repository at all: it takes a `remoteUrl` instead of
a `repositoryPath` and calls `Repository.ListRemoteReferences(remoteUrl, credentialsProvider)` to
list the remote's refs directly, so `ProjectService.CreateAsync` can validate the requested base
branch before the project's sandbox clone exists — see [docs/application.md](application.md)).
It also coordinates nothing between calls itself: two calls against the same sandbox running
concurrently (every ticket in a project shares one clone — see `MergeWithResolutionsAsync` below)
could corrupt the working directory or race on the same remote ref. `Application.Git.GitRepositoryLock`
is the fix, one layer up — every Application call site acquires it, keyed by `repositoryPath`,
around the git calls (and, where it matters, a status re-check) that must not interleave with
another such block. See [docs/application.md](application.md) for the specific race (a cancelled
ticket's branch delete racing an in-flight Coding stage's commit/push) this was added to close.

**`ListFilesAsync`/`ReadFileAsync` are read-only, and both take an optional `branchName`.** Every
other `IGitService` method either writes or talks to the remote; these two only ever read off
`repo.Info.WorkingDirectory`. Both resolve the caller's relative path against that working
directory via `Path.GetFullPath` and reject the call (`GitOperationException`) if the resolved
path doesn't stay under it — the only defense against a path-traversal attempt (e.g. `../../`)
reaching outside the sandbox. When `branchName` is given, that branch is checked out first (the
same `GetOrCreateBranch`+`Commands.Checkout` pattern `CommitFilesAsync` uses) so the read reflects
that branch's tip rather than whatever happens to already be checked out; passing `null` (the
Live Agent chat's usage — it isn't tied to any one ticket) preserves the original behavior
unchanged. `ReadFileAsync` additionally refuses well-known secret-bearing paths outright
(`.git/**`, `.env*`, `id_rsa*`/`id_ed25519*`, `*.pfx`/`*.pem`/`*.key`/`*.p12`), runs the remaining
content through `SecretRedactor`'s best-effort regex redaction (AWS-style keys, private-key
blocks, JWTs, common `password=`/`api_key=`-shaped assignments), and truncates past 20,000
characters. None of this is a guarantee against leaking a secret shaped differently than these
patterns — it's defense in depth, not a substitute for keeping real secrets out of a project's
repository. `ListFilesAsync` recursively walks the given path (skipping `.git`, `node_modules`,
`bin`, `obj`, `dist`, `.angular`) and returns every file's path relative to it, capped at 200
entries with a `"... truncated"` note appended when the cap is hit (prompting the caller to retry
with a narrower path) — so one call shows a whole subtree instead of just one directory level.
It used to list only the one directory the caller asked for (subdirectories shown as bare names
with a trailing `/`, requiring a fresh call per level to go deeper); that was changed after a
real, reproduced incident where a Research-stage tool-use loop burned most of its round budget
navigating six directory levels one `list_files` call at a time before ever reading a file,
exhausting the loop before it could produce an answer.

**`CommitFilesAsync` refuses to write to Git-internal or CI-workflow paths, independent of the
sandbox-escape check.** The Coding stage's file blocks are raw LLM output (see
[docs/application.md](application.md#workflow-integration)'s `BuildStagePrompt`/`ParseFileChanges`
description) grounded in a ticket description and repo content that are both untrusted from a
prompt-injection standpoint - `ResolveSandboxedPath` alone stops a path from escaping the clone,
but says nothing about *which* path inside it gets rewritten. `IsBlockedWritePath` (mirroring
`ReadFileAsync`'s own denylist, but for writes) rejects any path containing a `.git` segment or
starting with `.github/workflows/`, throwing `GitOperationException` before any file in the batch
is written - the one place a rewritten file in the target repository could get itself executed by
CI on push/PR, independent of the human approval gate the ticket's own changes still go through.
This is a coarse denylist, not a guarantee that every other file the model chooses to rewrite is
safe; the approval gate before merge remains the real backstop.

**Both are called by two different tool-use loops, not just the Live Agent chat.**
`Application/Git/GitReadOnlyTools` defines the `list_files`/`read_file` tool schema and dispatch
once and is shared by `LiveAgentChatService` (passing `branchName: null`) and by the
Research/Design/Coding pipeline stages via `OrchestrationService.RunStagePromptAsync` (passing
the ticket's own `BranchName` on every call — see
[docs/application.md](application.md#workflow-integration)). Each call is independently wrapped in
its own brief `GitRepositoryLock` scope rather than one held for an entire tool-use loop, so
passing a specific branch on every call is what lets a ticket-branch-specific caller safely share
one project's sandbox clone with everything else touching it, without ever holding the lock
across LLM latency. There used to be a third method here, `GetRepositorySnapshotAsync`, which
recursively read every file in a branch up front (capped at 100 files / 60,000 characters total)
so the Coding/Research/Design stages' single prompt could be grounded without any ability to ask
for a specific path mid-response; it's been removed now that those stages call `read_file`
themselves on demand, which removes the caps (and the risk of a large repo's alphabetical file
ordering silently dropping a file a stage actually needed) entirely.

**`MergeWithResolutionsAsync` finishes a conflicted merge as a real two-parent commit by resuming
LibGit2Sharp's own merge-in-progress state, not by re-merging after the fact.** It runs the same
trial merge `DetectMergeConflictsAsync` does (`repo.Merge(..., CommitOnSuccess: false,
FailOnConflict: false)`), which updates the working tree/index and leaves `MERGE_HEAD` set but
doesn't reset anything. Before checking any file, it captures `target.Tip.Sha` (the base branch's
*current, live* tip) once as `liveBaseTipSha`. Then, for each `repo.Index.Conflicts` entry it
finds, it looks up that file's path in the caller-supplied `resolutions` map
(`IReadOnlyDictionary<string, GitConflictResolution>`, content plus the base-tip SHA that
resolution was prepared against): no entry at all is collected as an unresolved path; an entry
whose `BaseTipSha` doesn't equal `liveBaseTipSha` is collected as a *stale* path instead - the
resolution isn't written to disk, since it was prepared against a base branch that's since moved
further; only a matching entry actually gets written and `Commands.Stage`d (clearing that index
conflict). Once every conflicting file is covered (no unresolved or stale paths), `repo.Commit(...)`
is called directly - LibGit2Sharp sees the still-set `MERGE_HEAD` and records it as a second parent
automatically, exactly like completing a conflicted `git merge` by hand (edit the file, `git add`,
`git commit`). The commit message is `"Merge branch '{sourceBranch}' into '{targetBranch}' (approved
by {mergerName})"`, and `mergerName` is also used as the commit's author/committer `Signature` - see
[docs/application.md](application.md) for where `ApprovalGateService.ApproveAsync` sources that name
from (the authenticated caller's login name, not the free-text `SubmitReviewRequest.ReviewerName`).
If anything is left unresolved *or* stale, the attempt is aborted
(`repo.Reset(ResetMode.Hard, target.Tip)`, same as `DetectMergeConflictsAsync`'s cleanup) rather
than left mid-merge, since every ticket in a project shares that project's one sandbox clone and a
real other ticket's pipeline stage could touch it next. `MergeStatus.UpToDate`/`FastForward`
short-circuit with nothing to commit (there's no merge state to finish), and a clean
`NonFastForward` merge just needs the one explicit commit call since `CommitOnSuccess: false` only
skips the auto-commit, not the merge itself. See [docs/application.md](application.md) for how
`ApprovalGateService.ApproveAsync` builds the `resolutions` map from the ticket's `Conflict`
records, and what it does differently for a stale path vs. an unresolved one.

**`DetectMergeConflictsAsync` also returns the base branch's tip** (`target.Tip.Sha`, read before
the trial merge's own `finally` resets the working tree back to it) as `GitMergeConflictResult.BaseTipSha`
- this is the SHA `ConflictResolutionService.DetectConflictsAsync` stamps onto each new `Conflict`
it creates, and exactly what `MergeWithResolutionsAsync` later compares its live tip against.

**`DeleteBranchAsync` pushes an empty source ref (`:refs/heads/{branchName}`) to delete the
remote branch** — the standard Git protocol convention, supported directly by LibGit2Sharp's
`Network.Push(Remote, string, PushOptions)` overload rather than needing a special "delete" API.
It also guards against deleting the branch that's currently checked out in the sandbox (which
LibGit2Sharp would otherwise reject): if `repo.Head` is sitting on the branch being deleted, it
checks out `baseBranchName` first. The local branch ref is removed after the remote delete
succeeds; if it was never fetched/created locally in the first place, that's a silent no-op
rather than an error.

**Projects are cloned into a server-managed sandbox, not pointed at a path someone else prepared.**
`LibGit2SharpGitService.CloneAsync(projectId, remoteUrl, accessToken, progress, ct)` computes
`{Git:SandboxRoot}/{projectId}` (resolved against `IHostEnvironment.ContentRootPath` when
`SandboxRoot` is relative), clones there with `Repository.Clone`, and returns the resulting
path for `ProjectService` to store via `Project.MarkCloned`. `PushAsync`/`FetchAsync` open
that same sandbox and talk to its `origin` remote. All three wrap `LibGit2SharpException` as
the Application's `GitOperationException` (→ HTTP 422) instead of letting it bubble to a
generic 500, since a bad URL/token is a client-facing, actionable error.

**When `progress` is given, `CloneAsync` wires it into `CloneOptions.FetchOptions.OnTransferProgress`
and `CloneOptions.OnCheckoutProgress`** - LibGit2Sharp callbacks that fire throughout the clone
(object-transfer phase, then working-directory checkout), reporting a `GitCloneProgress` (received/
total objects and bytes, checkout completed/total steps) each time. Both callbacks can fire many
times a second for a large repo, far more often than a progress bar needs to redraw, so a
`Stopwatch`-based throttle only actually calls `progress.Report(...)` at most every ~250ms (always
letting the final checkout step through, so the bar visibly reaches 100%). `ProjectService`'s
detached clone task wraps this in an `IProgress<GitCloneProgress>` that publishes each report as a
`ProjectEventTypes.ProjectCloneProgress` SSE event (see [docs/application.md](application.md)) -
`OnTransferProgress` also returns `!cancellationToken.IsCancellationRequested`, so a cancelled
clone actually stops instead of running to completion regardless.

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

**`IBackgroundTaskRunner` (`Infrastructure/BackgroundTasks/BackgroundTaskRunner.cs`) is the
codebase's first fire-and-forget mechanism** — registered `AddSingleton` since it only wraps
`IServiceScopeFactory` (itself effectively a singleton), unlike everything else in this file's DI
registration, which is `AddScoped` because `TeamPilotDbContext` is. `Run(work)` fires `work` on
the thread pool inside a fresh `IServiceScopeFactory.CreateScope()`, passing that scope's own
`IServiceProvider` in rather than anything from the caller's scope, and catches/logs any
exception `work` doesn't handle itself as a last-resort safety net. **Wraps its `Task.Run` in
`ExecutionContext.SuppressFlow()`** - without it, `Task.Run` captures and flows the calling
request's `ExecutionContext`, which carries `IHttpContextAccessor`'s `AsyncLocal` `HttpContext`
along with it; `TeamPilot.API/Program.cs`'s `ICurrentUserContext` factory would then see a
non-null `HttpContext` inside this "detached" task and wrongly resolve
`HttpContextCurrentUserContext` instead of `SystemCurrentUserContext` - and since
`AddHttpContextAccessor()` makes ASP.NET Core pool and reuse `HttpContext` instances, that stale
reference can already belong to an unrelated later request (e.g. the board's own poll) by the
time the delegate's first `await` returns, so `IProjectAccessGuard` ends up checking against
whoever currently owns that recycled object instead of the user who actually triggered the run.
Currently used by
`OrchestrationService.RunPipelineDetached`, the single point every pipeline-(re-)starting entry
point (`TicketsController.Start`, `ApprovalGateService`'s `RequestChanges` branch,
`TicketQuestionService.AnswerAsync`/`RetryAsync`) goes through instead of awaiting
`RunPipelineAsync` inline (see [docs/application.md](application.md)) — the delegate resolves its
own dependencies (`IOrchestrationService`, `ITicketRepository`, etc.) from the given
`IServiceProvider`, never from fields injected into the caller itself, since the caller's own
scope (and scoped `DbContext`, and its request's `CancellationToken`) is disposed/cancelled once
its request returns, before the detached work runs. **`ProjectService`'s private
`StartCloneDetached` follows the exact same shape** for a project's initial clone (see
[docs/application.md](application.md)) - the one difference is it has no analog to
`IPipelineRunTracker`'s `MarkRunning`/`MarkFinished` pair, since `Project.Status` itself (persisted,
not in-memory) is the running/finished signal here, and it's already saved as `Cloning` before
`StartCloneDetached` even dispatches the background work.

**`IPipelineRunTracker` (`Infrastructure/BackgroundTasks/PipelineRunTracker.cs`) is the second
fire-and-forget-adjacent singleton, also `AddSingleton`** — a plain in-memory
`ConcurrentDictionary<Guid, int>` reference-counting how many runs are currently in flight per
ticket (`MarkRunning`/`MarkFinished`/`IsRunning`), not a persisted flag, so an app restart can
never leave it stuck reporting a run that no longer exists (the actual work died with the process
too). `RunPipelineDetached` calls it directly off its own injected field rather than resolving it
fresh from the background scope's `IServiceProvider` like everything else in that delegate - safe
specifically because it's a true singleton with no scope of its own to outlive, unlike the scoped
dependencies (`ITicketRepository` etc.) the delegate must still resolve fresh. See
[docs/application.md](application.md) for how `TicketDto.PipelineRunning` surfaces this to callers.

**`IProjectEventBroadcaster` (`Infrastructure/RealTime/ProjectEventBroadcaster.cs`) is the third
singleton in this group, backing the SSE stream `ProjectEventsController` exposes.** Internally a
`ConcurrentDictionary<Guid /* projectId */, ConcurrentDictionary<Guid /* subscriptionId */,
Channel<ProjectEvent>>>` - `Subscribe` creates a small bounded `Channel<ProjectEvent>`
(`BoundedChannelFullMode.DropOldest`, capacity 16) per SSE connection, registers it under its
project, and unregisters it in a `finally` once the connection's `CancellationToken` (the
request's own `HttpContext.RequestAborted`) fires; `Publish` just fans a `ProjectEvent` out to
every channel currently registered for that project via a non-blocking `TryWrite` - a stalled
reader drops its own oldest queued event rather than a slow subscriber ever blocking a publisher.
Deliberately not durable, same reasoning as `IPipelineRunTracker`: a subscriber that wasn't
connected when an event fired just relies on its own next `GET` (or the client-side safety-net
poll - see [docs/frontend.md](frontend.md)) to pick up current state, so there's nothing to
replay after a restart.

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
- `TicketAgentEventConfiguration` follows `TicketQuestionConfiguration`'s exact shape (cascade on
  `TicketId`, restrict on nullable `AgentId`) with an added `Role` column, stored as a string like
  `Kind`. An index on `(TicketId, CreatedAtUtc)` backs `TicketAgentEventRepository.ListByTicketAsync`'s
  chronological listing. The `AddTicketAgentEvents` migration only adds the new table - no
  backfill, same reasoning as `AddStageExecutions`. `InputTokens`/`OutputTokens`/`DurationMs` are
  plain nullable `int` columns added by the follow-up `AddTicketAgentEventUsage` migration, left to
  EF's default conventions (no explicit Fluent API needed, same as `WorkflowStage.MaxLoopIterations`)
  since a stage's token counts and duration need no string conversion or index of their own.
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
// LibGit2SharpGitService.CommitFilesAsync — opens a fresh Repository handle per call
using var repo = OpenRepository(repositoryPath);
var branch = GetOrCreateBranch(repo, branchName);
Commands.Checkout(repo, branch);
// Every path is resolved (and validated as staying inside the sandbox) before any file is
// written, so a bad path can't leave a half-written working directory behind.
foreach (var (relativeFilePath, fileContent) in fileContentsByRelativePath)
{
    File.WriteAllText(fullPathsByRelativePath[relativeFilePath], fileContent);
    Commands.Stage(repo, relativeFilePath);
}
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
`MergeWithResolutionsAsync` finding conflicting files it can't resolve (or can't trust - see its
own section above) is not one of these cases - that's an expected, common outcome it reports back
via its return value (`GitMergeResolutionResult`'s separate `UnresolvedFilePaths`/`StaleFilePaths`
lists), not an exception; `ApprovalGateService` is the one that turns those into a client-facing
`UnresolvedConflictsException`/`StaleConflictResolutionException` (both → 409).
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
`FetchAsync`, `RemoteBranchExistsAsync`, and the remote-delete push inside `DeleteBranchAsync`.
Local-only operations
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
