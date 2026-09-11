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

**`IGitService` takes a `repositoryPath` per call, not a single configured path.** Since each
`Project` owns its own sandbox clone, the service holds no per-project state; every method
(`CloneAsync`, `PushAsync`, `FetchAsync`, `EnsureBranchAsync`, `CommitFileAsync`, `GetDiffAsync`,
`DetectMergeConflictsAsync`, `MergeBranchAsync`) opens and disposes its own
`LibGit2Sharp.Repository` handle per call (`CloneAsync` is the one exception — it creates the
repository rather than opening an existing one).

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
`DbUpdateException` (constraint violations) and `HttpRequestException` (Claude API failures)
currently bubble up to the API's `GlobalExceptionHandler` and are mapped generically to HTTP
500. Two places Infrastructure *does* translate an error: `ExternalIdentityValidator` wraps any
token-validation failure as the Application's own `AuthenticationFailedException` (→ 401), and
`LibGit2SharpGitService`'s remote operations (`CloneAsync`/`PushAsync`/`FetchAsync`) wrap
`LibGit2SharpException` as `GitOperationException` (→ 422) — local-only Git operations
(commit/diff/detect-conflicts/merge) still let `LibGit2SharpException`/`InvalidOperationException`
bubble to 500/409, since those failures point at a server-side bug rather than bad user input.

## Future considerations

- **Translate `DbUpdateException` into domain-meaningful errors** (e.g. a unique-constraint
  violation on `User.Email` surfacing as a 409 rather than a generic 500) — not done yet
  because no code path currently allows a client to trigger one that isn't already caught by
  FluentValidation first.
- **Resiliency policies** for the Claude HTTP client and LibGit2Sharp calls (retry-on-transient,
  circuit breaker) — noted as a one-line addition (`AddStandardResilienceHandler()`) during
  design but not implemented, since no production load exists yet to tune it against.
- **Asymmetric JWT signing** (RS256) if a second service ever needs to validate TeamPilot's
  access tokens independently.
- **Multi-provider LLM support**: the `ILlmConnector` seam is ready; only `ClaudeLlmConnector`
  exists.
- **Background job runner** for actually executing `PipelineRun`s, once CI/CD needs to do real
  work instead of just tracking status.
