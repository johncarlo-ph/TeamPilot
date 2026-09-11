# Cross-Cutting Concerns

[← Back to README](../README.md)

Concerns here span every module rather than belonging to one layer.

## Shared utilities

**Logging.** Structured logging via `Microsoft.Extensions.Logging` (`ILogger<T>`) throughout
Application and Infrastructure — no `Console.WriteLine` in production code paths. Log messages
use structured placeholders (`logger.LogInformation("Agent {AgentId} ({Role}) produced output for ticket {TicketId}...", agent.Id, agent.Role, ticket.Id)`)
rather than string interpolation, so log fields stay queryable in whatever sink is configured.
`appsettings.json`'s `Logging:LogLevel` currently sets `Default: Information`,
`Microsoft.AspNetCore: Warning`.

**Audit logging.** A dedicated, append-only mechanism separate from general application
logging: `IAuditLogger.LogAsync(eventType, userId, detail, ipAddress)` writes an
`AuditLogEntry` row on every login (success/failure), logout, token refresh, and detected
refresh-token reuse. It participates in the same `IUnitOfWork.SaveChangesAsync()` call as the
operation it's logging (not a separate transaction), so an audit entry and the event it
describes are always persisted atomically. Readable only by Admins, via
`GET /api/audit-log`.

**Constants.** There is no single "constants" file — fixed sets of values are modeled as
enums (`TicketStatus`, `UserRole`, `AuditEventType`, etc.) rather than string/int constants,
so the compiler enforces exhaustiveness at usage sites (e.g. a `switch` over `ReviewDecision`
in `ApprovalGateService` has a `default` case for genuinely unreachable values, but every real
case is handled explicitly).

**Error messages.** `Common.Exceptions.ValidationException` carries a
`IReadOnlyDictionary<string, string[]>` of field → messages, populated from FluentValidation's
`ValidationFailure`s and surfaced under the `errors` key of the `ProblemDetails` response body
— the same shape for every validation failure across every endpoint.

## Testing strategy

**Today:**
- **Unit tests** (`tests/TeamPilot.Domain.Tests`, `tests/TeamPilot.Application.Tests`) using
  xUnit and Moq. Domain tests exercise entity invariants and state transitions directly, with
  no mocking needed. Application tests mock every repository/service dependency and assert on
  the service's own logic (guard calls, role checks, mapping) in isolation.
- Naming convention: `MethodName_StateUnderTest_ExpectedBehavior` (e.g.
  `SubmitReviewAsync_WhenAnalystSubmitsApprove_ThrowsForbiddenException`).
- Run with `dotnet test TeamPilot.slnx`.

**Not yet implemented — recommended before production use:**
- **Integration tests** against a real (or Testcontainers-provisioned) SQL Server instance, to
  catch issues unit tests with mocked repositories can't (the `ValueGeneratedNever` /
  cascade-path issues documented in [docs/infrastructure.md](infrastructure.md) were both only
  discoverable by actually running EF Core against a real database).
- **API-level tests** using `Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory`, to
  exercise the full HTTP pipeline (auth, routing, model binding, `GlobalExceptionHandler`)
  end-to-end without a browser.
- **End-to-end tests** once the Angular frontend exists, covering the full login → create
  ticket → approve → merge flow through the UI.

## Security practices

- **Input validation**: FluentValidation on every request DTO (see
  [docs/application.md](application.md)); enum values are validated at model-binding time
  (`JsonStringEnumConverter` rejects unknown enum strings with a 400 before a controller runs).
- **SQL constraints, not just application checks**: unique indexes on `User.Email`,
  `RefreshToken.TokenHash`, `(UserId, ProjectId)` on `UserProjectAssignment`,
  `(AgentId, Type, Version)` on `Instruction`, `(TicketId, CommitHash)` on `Commit`; foreign
  keys with deliberate `Cascade`/`Restrict`/`SetNull`/`NoAction` behavior per relationship (see
  [docs/infrastructure.md](infrastructure.md) for the cascade-path caveats). These exist
  alongside, not instead of, EF/FluentValidation checks.
- **Safe defaults**:
  - New users are created with **zero roles** — an Admin must explicitly grant access
    (secure-by-default, not "grant Analyst by default and lock down later").
  - Refresh tokens are rotated on every use and the **entire session family is revoked** if a
    already-rotated token is ever presented again (theft mitigation).
  - Disabling a user's account immediately revokes all of their active refresh tokens, not just
    future logins.
  - The refresh token is never accessible to JavaScript (`HttpOnly` cookie); only the
    short-lived access token is held client-side.
  - `app.UseHttpsRedirection()` is always active.
  - No local password authentication exists at all — removes an entire class of credential-
    stuffing/password-reset attack surface by construction.
- **Known, deliberate trade-off**: `User.Email` is stored in **plain text** (not hashed) so
  Admins can identify accounts directly — this was an explicit product decision that
  intentionally supersedes an earlier design that stored only a non-reversible hash. If privacy
  requirements change again, revisit `docs/domain.md`'s `User` entity and the unique index on
  `Email`.
- **Secrets never committed**: `Jwt:SigningKey`, `Auth:Providers:*:Audience`, `Llm:ApiKey` are
  all empty in `appsettings.json` and expected to be supplied via `dotnet user-secrets` in
  development or a real secret store (Key Vault, environment variables, etc.) in production.

## Deployment workflow

**Build artifacts.** Standard ASP.NET Core publish:

```bash
dotnet publish src/TeamPilot.API -c Release -o ./publish
```

**Environment configuration.** `appsettings.json` holds non-secret defaults;
`appsettings.{ASPNETCORE_ENVIRONMENT}.json` overrides per environment; secrets are supplied
out-of-band (user-secrets in dev, a real secret store in any deployed environment) — never
committed alongside the non-secret config.

**Migrations.** Two supported approaches:
- Run `dotnet ef database update` as a deploy step against the target database (simplest, but
  requires the deploying identity to have schema-change permissions).
- Generate a SQL script for DBA review/execution instead of running migrations directly against
  production: `dotnet ef migrations script --project src/TeamPilot.Infrastructure --startup-project src/TeamPilot.API`.

**CI/CD.** As noted in the [README](../README.md#cicd), no pipeline exists in this repository
yet. The recommended minimum before any deployment automation: a build+test gate on every pull
request, then a separate, explicitly-triggered publish+migrate+deploy step — not implemented
as of this writing, and this document will be updated once one exists rather than describing
one that doesn't.
