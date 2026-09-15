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
`AuditLogEntry` row for a fixed set of `AuditEventType`s. The auth flow (login success/failure,
logout, detected refresh-token reuse) calls this directly with an explicit `userId`/`ipAddress`
since those requests are anonymous. Every other state-changing use case (creating/updating a
project, ticket, or agent; adding/removing/reordering a workflow stage or configuring its
loop-back; submitting a review, changing a user's roles, etc.)
instead calls the convenience overload `IAuditLogger.LogActionAsync(eventType,
detail)`, which reads the caller's id and IP off `ICurrentUserContext` so services don't have to
thread those values through every call site. A silent background token refresh is deliberately
**not** logged - it isn't a user-initiated action. Either way, the entry participates in the same
`IUnitOfWork.SaveChangesAsync()` call as the operation it's logging (not a separate transaction),
so an audit entry and the event it describes are always persisted atomically. Readable only by
Admins, via `GET /api/audit-log`, which resolves each entry's `UserId` to a display name
(`AuditLogEntryDto.UserName`) so the UI never shows a raw account id.

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
- **End-to-end tests** covering the full login → create ticket → approve → merge flow through
  the UI. The Angular frontend (`src/TeamPilot.UI`) now exists, so this is unblocked, but no
  e2e suite has been added yet — today its automated coverage is a small representative sample
  of unit specs (`AuthService`, `StatusBadge`) run via `ng test`, not full-flow coverage.

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
- **CORS is origin-allowlisted, not wide open**: the `AngularClient` policy (see
  [docs/api.md](api.md#cross-origin-requests-cors)) pairs `AllowCredentials()` with an explicit
  `Cors:AllowedOrigins` list rather than `AllowAnyOrigin()` — required because the refresh-token
  cookie flow is credentialed, and it also means an unlisted origin can't call the API from a
  browser at all, credentialed or not.
- **Prompt-injection defense in depth for the agent pipeline**: a ticket's title/description,
  review comments, a human's answer to a blocking question, and one stage's output handed to the
  next are all untrusted text from an LLM's perspective, with no structural system/user separation
  in `OrchestrationService.BuildStagePrompt` (everything lands in one prompt string — see
  [docs/application.md](application.md#workflow-integration)). That text is wrapped in tags
  (`<ticket_description>`, `<review_feedback>`, `<human_answer>`, `<previous_stage_output>`,
  `<your_previous_output>`) with an explicit instruction to treat tagged content as data, never as
  new instructions — a prompting-level mitigation, not a guarantee. The one path where a stage's
  raw output becomes a real side effect without a human in the loop first — Coding's `<file>`
  blocks being committed and pushed — is additionally constrained at the Git layer: `IGitService`
  refuses to write to a `.git` path or under `.github/workflows/` (see
  [docs/infrastructure.md](infrastructure.md)), so an injected instruction can't use that path to
  persist itself into the target repository's CI. The real backstop for everything else a Coding
  stage might rewrite remains the human approval gate before merge (`ApprovalGateService.ApproveAsync`).
- **Every per-ticket `GET .../{ticketId}/...` listing endpoint checks project access in the
  Application layer, not just authentication**: `ReviewsController`, `TicketQuestionsController`,
  and `TicketAgentEventsController` each route on a bare `ticketId` (no `projectId` segment to
  check against at the API boundary), so `IApprovalGateService.ListByTicketAsync`,
  `ITicketQuestionService.ListByTicketAsync`, and `ITicketAgentEventService.ListByTicketAsync`
  each load the ticket and call `IProjectAccessGuard.EnsureAccessAsync(ticket.ProjectId)` before
  reading anything — closing a gap where all three controllers used to call their repository
  directly, letting any authenticated user who knew or guessed a ticket's id read its reviews,
  questions, or agent-event log regardless of project membership.

## Deployment workflow

**Build artifacts.** Two separately deployed artifacts now that the frontend exists:

```bash
dotnet publish src/TeamPilot.API -c Release -o ./publish
```

```bash
cd src/TeamPilot.UI && npm run build   # ng build, production config — outputs to dist/TeamPilot.UI
```

The Angular build is a static site (no Node server required at runtime) and can be hosted from
any static host/CDN in front of, or alongside, the API's own origin.

**Environment configuration.** `appsettings.json` holds non-secret defaults;
`appsettings.{ASPNETCORE_ENVIRONMENT}.json` overrides per environment; secrets are supplied
out-of-band (user-secrets in dev, a real secret store in any deployed environment) — never
committed alongside the non-secret config. On the frontend side,
`src/TeamPilot.UI/src/environments/environment.ts` (production) holds the deployed API's
`apiBaseUrl` and the Google/Microsoft OAuth client IDs for that environment — these are public
client identifiers, not secrets, but still must match whatever origin they're deployed to (see
`Cors:AllowedOrigins` above and the OAuth provider's own redirect/origin configuration).

**Reverse proxies must not buffer or time out the SSE endpoint.** `GET /api/projects/{projectId}/events`
(`ProjectEventsController` — see [docs/api.md](api.md)) is a long-lived `text/event-stream`
connection, one per open board/ticket-detail page, not a request-response call. A reverse proxy
placed in front of Kestrel (nginx, an API gateway, a load balancer) must have response buffering
disabled and its idle-connection timeout raised for this route, or it will hold every event until
the connection closes (defeating the point) or drop the connection outright. The controller
already sets `X-Accel-Buffering: no` (nginx's own opt-out) and sends a heartbeat comment every 15s
to keep the connection visibly active, but proxy-side configuration is still required for
whichever reverse proxy actually fronts a given deployment.

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
