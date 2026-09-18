# API Module (`TeamPilot.API`)

[← Back to README](../README.md)

## Purpose

`TeamPilot.API` is the Presentation layer: ASP.NET Core controllers that translate HTTP
requests into Application service calls, the authentication/authorization pipeline, Swagger
documentation, and centralized exception-to-HTTP-response mapping. It is the composition root
— `Program.cs` is where `AddApplication()` and `AddInfrastructure(configuration)` are wired
together and where the JWT bearer pipeline is configured.

## Architectural decisions

**Controllers are thin.** Every controller action does validation-by-delegation (the service
validates), authorization-by-attribute-or-service (see below), and maps a service call directly
to an `ActionResult<T>` — there is no business logic in a controller.

**Authorization is enforced in two places, deliberately.** `MapControllers().RequireAuthorization()`
in `Program.cs` makes every endpoint require a valid bearer token by default; `[AllowAnonymous]`
opts out the three unauthenticated `AuthController` actions (`login`, `refresh`, `logout`).
Role checks are `[Authorize(Roles = "Admin")]` at the controller/action level for admin-only
surfaces (`UsersController`, `AuditLogController`, `ProjectsController.Create`/`Update`) — but
**project-scoping and the Approve-role rule are enforced in the Application layer**, not here
(see [docs/application.md](application.md)), because they depend on data (which project, which
decision) that a static attribute can't express.

**`ICurrentUserContext` is implemented here, not in Infrastructure**, specifically because it
wraps `IHttpContextAccessor` — an ASP.NET Core hosting concern that Infrastructure shouldn't
need to reference just to answer "who is the current user."

**A single `GlobalExceptionHandler`** (implementing `IExceptionHandler`) is the only place that
decides HTTP status codes from exceptions — no controller has its own try/catch for this.

## Code style notes

- Route conventions: nested resources for creation/listing where the parent is meaningful
  (`POST /api/sprints/{sprintId}/tickets`, `POST /api/projects/{projectId}/sprints`), flat routes
  for single-resource operations where the id is already globally unique (`GET /api/tickets/{id}`).
- Enums serialize as their string names (`JsonStringEnumConverter`, registered globally, plus a
  Swagger `EnumSchemaFilter` so the generated schema matches) — never raw integers.
- XML doc comments are enabled (`GenerateDocumentationFile`) and feed Swagger's
  `IncludeXmlComments`; `CS1591` (missing XML comment) is suppressed rather than forcing every
  public member to have one.

## Controllers

| Controller | Base route | Notes |
|---|---|---|
| `AuthController` | `/api/auth` | `[AllowAnonymous]` login/refresh/logout; `GET /me` requires auth but no specific role |
| `ProjectsController` | `/api/projects` | `Create`/`Update`/`Remove` are `[Authorize(Roles="Admin")]`; `List`/`GetById` are project-scoped in the service. `Create` persists the project immediately (`status: "Cloning"`) and returns right away - the actual clone of `RemoteUrl` runs detached, so a bad URL/token no longer fails the `Create` request itself (400/422 from `Create` now only comes from request validation). Progress and the eventual outcome (`Ready`/`Failed`, with `cloneFailureReason` set on failure) are reported via `ProjectEventTypes.ProjectCloneProgress` on `GET /projects/{id}/events` instead (see `ProjectEventsController` below and [docs/application.md](application.md)). `DELETE /{id}` hides the project (`Project.IsRemoved`) rather than deleting its repository or history, returning `204`; 409 (`ProjectHasActiveTicketsException`) if any of its sprints has a ticket `InProgress` or `ForReview`. `ProjectDto` is just identity and clone status now - name, description, remote URL, status, clone failure reason; base branch, sprint dates/goal, and ticket-status counts all moved to `SprintDto` below |
| `SprintsController` | `/api/projects/{projectId}/sprints`, `/api/sprints/{id}` | `POST /projects/{projectId}/sprints` and `GET /projects/{projectId}/sprints` create/list a project's sprints; `GET`/`PUT`/`DELETE /sprints/{id}` operate on one. `Create`/`Update`/`Remove` are `[Authorize(Roles="Admin")]`, same pattern as `ProjectsController`. `Create`/`Update` check the requested `baseBranch` against the *parent project's* remote (422 `GitOperationException` if it doesn't exist - the same check `ProjectsController` used to run for a project's own base branch); `baseBranch` defaults to `"main"` when omitted on create. `SprintDto` carries `name`, `baseBranch`, `sprintStartDate`/`sprintEndDate`/`sprintGoal` (optional, purely informational; 400 if `sprintEndDate` is before `sprintStartDate`), and `ticketStatusCounts` (the project list's old per-status summary, now per-sprint). `DELETE /{id}` hides the sprint rather than deleting its tickets, returning `204`; 409 (`SprintHasActiveTicketsException`) if it has a ticket `InProgress` or `ForReview` |
| `TicketsController` | `/api/sprints/{sprintId}/tickets`, `/api/projects/{projectId}/tickets`, `/api/projects/{projectId}/tickets/backlog`, `/api/tickets/{id}/...` | Ticket board CRUD, scoped to a sprint (`POST`/`GET /sprints/{sprintId}/tickets` - the board's own routes) - plus a project-wide, read-only `GET /projects/{projectId}/tickets` across every sprint (used by the Agents page's workflow-lock check and the Live Agent's ticket tool, not the board itself), and `POST`/`GET /projects/{projectId}/tickets/backlog` for a project's backlog (tickets with no sprint yet - `sprintId: null` in the response; same `CreateTicketRequest` body/validation as the sprint-scoped `POST`, but no `ProjectNotReadyException` retry needed once created - see [docs/application.md](application.md)). `POST /tickets/{id}/assign-sprint { sprintId }` moves a backlog ticket into a sprint - 404 if `sprintId` doesn't belong to the ticket's own project, 409 (`TicketAlreadyAssignedToSprintException`) if the ticket already has one. `POST /tickets/{id}/start` validates the ticket then kicks off the project's configured agent workflow (`OrchestrationService.StartPipelineAsync` → `RunPipelineDetached`, default Research→Design→Coding→Testing) detached from the request and returns immediately with the current `TicketDto` - a run can take minutes (multiple LLM/Git calls per stage), so it's no longer awaited inline, and a client disconnecting (e.g. a page refresh) can't abort it mid-flight; poll `GET /tickets/{id}` (or the board's own poll) to observe the eventual `InProgress`/`Blocked`/`ForReview` outcome. Every `TicketDto`/`TicketDetailDto` also carries `pipelineRunning`, since `status` alone can't tell a caller whether a run is actively executing right now versus an `InProgress` ticket just sitting idle (see `IPipelineRunTracker` in [docs/application.md](application.md)), and both also carry `sprintId` alongside `projectId` (the latter denormalized - see [docs/domain.md](domain.md#sprint)). `POST /tickets/{id}/cancel` abandons a pre-merge ticket (`ToDo`/`InProgress`/`ForReview`/`Blocked`) with an optional reason, terminal, and deletes its linked branch from Git in the same call if it has one; `POST /tickets/{id}/retry` unblocks a ticket blocked by a Git/LLM failure and re-runs the pipeline detached the same way, returning the unblocked `TicketDto` (409 if it's blocked by a clarifying question instead - use `TicketQuestionsController` for that). `POST /sprints/{sprintId}/tickets` requires a non-empty `acceptanceCriteria` alongside `title`/`description` (400 from request validation if blank) - every `TicketDto`/`TicketDetailDto` carries it back, and the pipeline agents read it as part of their prompt (see [docs/application.md](application.md)) |
| `AgentsController` | `/api/projects/{projectId}/agents`, `/api/agents/{id}` | Read/update only — list, get, edit configuration/status. No `POST` here: the 4 default pipeline agents and the standing `LiveAgent` are system-provisioned per project; a custom agent is created through `WorkflowController` instead |
| `WorkflowController` | `/api/projects/{projectId}/workflow` | The project's admin-configurable agent workflow. `GET` (stages) and `GET /unscheduled-agents` are open to any authenticated user with project access; `POST /agents` (blank custom agent, not yet scheduled), `POST /stages` (schedule an unscheduled agent - rejects incomplete instructions), `DELETE /stages/{id}`, `PUT /order`, and both loop-back endpoints (`PUT`/`DELETE .../loop-back`) are `[Authorize(Roles="Admin")]` and 409 while the project has an `InProgress` ticket; `DELETE /agents/{agentId}` (also Admin-only) permanently deletes a custom agent, but only if it's unscheduled and has never been assigned to a ticket - not subject to the `InProgress` lock, since an unscheduled agent can't affect a running ticket |
| `InstructionsController` | `/api/agents/{agentId}/instructions` | Versioned instructions |
| `LiveAgentChatController` | `/api/projects/{projectId}/live-agent/conversations`, `.../conversations/{conversationId}`, `.../conversations/{conversationId}/messages`, `.../messages/{messageId}/approve-ticket`, `.../messages/{messageId}/reject-ticket` | A project can have any number of Live Agent chat sessions. `GET /conversations` lists them (most recently updated first) - every project member sees the full list, regardless of who started each one. `POST /conversations { title? }` starts a new session for the caller (blank/omitted title falls back to `"New chat"`); `PUT /conversations/{conversationId} { title }` renames one - any project member can rename any session. `GET .../messages` returns one session's chat history (oldest first); `POST .../messages { content }` sends a user message into that session, runs the Live Agent's bounded tool-use loop, and returns its assistant reply - including `proposedTicketTitle`/`proposedTicketDescription`/`proposedTicketAcceptanceCriteria` when the model drafted a ticket (all three are required from the model's `propose_ticket` tool call, since `Ticket.Create` requires acceptance criteria). Every message also carries `senderName` (the sending user's display name for a user message, `null` for an assistant message), `createdTicketId` (set once the draft is approved, otherwise `null`), and `ticketRejected` (`true` once dismissed). `POST .../messages/{messageId}/approve-ticket { sprintId, title, description?, acceptanceCriteria }` creates the real ticket using the given fields - normally the message's own draft, but the chat UI lets the user edit them first (a pencil button on the approval card switches it to an editable title/description/acceptance-criteria form), so the request may carry edited values instead (400 if `acceptanceCriteria` is blank, or if `sprintId` is empty; 404 if `sprintId` doesn't belong to this conversation's project) - since the conversation itself is project-scoped but a ticket needs a sprint, the frontend sends whichever sprint's board the chat panel is open alongside - and stamps `createdTicketId` on the message, overwriting its `proposedTicketTitle`/`proposedTicketDescription`/`proposedTicketAcceptanceCriteria` with whatever was actually submitted, in the same call; `POST .../messages/{messageId}/reject-ticket` (no body) dismisses the draft without creating anything, stamping `ticketRejected` instead. Both are idempotent for a repeat of the same decision (just returns current state) and 409 if the *other* decision was already made on that message |
| `InstructionTemplatesController` | `/api/instruction-templates` | Global, not project-scoped. `GET`/`GET/{id}` open to any authenticated user; `POST`/`PUT/{id}`/`DELETE/{id}` are `[Authorize(Roles="Admin")]` per-action |
| `GitController` | `/api/git/branches`, `/api/git/branches/{ticketId}`, `/api/git/branches/exists`, `/api/projects/{projectId}/git/{diff,conflicts}` | `POST /branches`, `DELETE /branches/{ticketId}`, and `GET /branches/exists` delegate to `ITicketService` (which enforces project access internally); `diff`/`conflicts` are thin `IGitService` pass-throughs with an explicit `IProjectAccessGuard` check. `POST /branches` returns 409 (`BranchAlreadyLinkedException`) if the branch name is already linked to a different ticket in the same project, or 409 (`TicketNotAssignedToSprintException`) if the ticket is still in the backlog (no sprint to cut a base branch from); `DELETE /branches/{ticketId}` returns 409 (`InvalidTicketStateTransitionException`) unless the ticket is `Cancelled` |
| `ReviewsController` | `/api/tickets/{ticketId}/reviews` | Approval-gate submissions. `GET` lists a ticket's review history via `IApprovalGateService.ListByTicketAsync` (loads the ticket, checks `IProjectAccessGuard`, then reads `IReviewRepository`) - this route carries only a `ticketId`, not a `projectId`, so the access check happens inside the service rather than at the API boundary; `GET` used to read `IReviewRepository` directly with no such check at all |
| `TicketQuestionsController` | `/api/tickets/{ticketId}/questions` | `GET` lists a ticket's clarifying-question/decision/failure history via `ITicketQuestionService.ListByTicketAsync` (same load-ticket-then-guard pattern as `ReviewsController`'s `GET`, for the same reason - it used to read `ITicketQuestionRepository` directly with no access check); `POST /{questionId}/answer` answers the pending question or decision, unblocks the ticket, and re-runs the pipeline detached (`IOrchestrationService.RunPipelineDetached`), returning the unblocked `TicketDto` immediately rather than the eventual pipeline outcome - a decision whose answer is "cancel this" is resolved by the human calling `POST /tickets/{id}/cancel` directly instead, never through this endpoint |
| `TicketAgentEventsController` | `/api/tickets/{ticketId}/agent-events` | `GET` lists a ticket's full agent lifecycle log (`Started`/`Completed`/`Blocked`/`Failed` per stage invocation, oldest first) for the ticket detail page's live agent log, via `ITicketAgentEventService.ListByTicketAsync` - same load-ticket-then-guard pattern as `ReviewsController`/`TicketQuestionsController`'s `GET`. Each `TicketAgentEventDto` also carries `inputTokens`/`outputTokens`/`durationMs` (all nullable) - set on `Completed`/`Blocked`, `durationMs` only on `Failed`, none on `Started` - see [docs/application.md](application.md) |
| `ConflictsController` | `/api/tickets/{ticketId}/conflicts`, `/api/conflicts/{id}/...` | Conflict detection/resolution |
| `ProjectEventsController` | `/api/projects/{projectId}/events` | `GET` opens a long-lived `text/event-stream` (SSE) connection - the board and ticket-detail pages' real-time refresh, and the project list's clone-progress UI (see [docs/application.md](application.md) for `IProjectEventBroadcaster` and [docs/frontend.md](frontend.md) for the consumers). Authenticated and project-access-checked the same as any other endpoint (the frontend consumes it via `fetch`, not native `EventSource`, specifically so the usual bearer-header auth applies unchanged - see docs/frontend.md). Sends a `: heartbeat` comment every 15s so an idle connection isn't dropped by an intermediary proxy; a production deployment behind a reverse proxy must disable response buffering/timeouts for this route (the controller already sets `X-Accel-Buffering: no` for nginx). Its own `JsonSerializerOptions` explicitly adds `JsonStringEnumConverter` (mirroring `Program.cs`'s `AddJsonOptions`) since this controller writes raw SSE frames outside MVC's own JSON formatting - needed now that a `ProjectCloneProgress` event's payload carries a real enum (`CloneProgressPayload.Status`), not just the plain string `Type` every event has always carried |
| `DashboardController` | `/api/dashboard/summary` | `GET` returns the projects landing page's summary rollup: `activeSprintCount`, global `ticketStatusCounts` (Cancelled excluded, same convention as `SprintDto.ticketStatusCounts`), and `needsAttention` (`blockedTickets`/`ticketsForReview` - each `{id, title, status, projectId, projectName, sprintId, sprintName}`, the latter two `null` for a backlog ticket - and `sprintsAtRisk` - `{id, name, projectId, projectName, sprintEndDate, unfinishedTicketCount}` for sprints ending within 3 days or already past-due that still have an `InProgress`/`ForReview`/`Blocked` ticket). No route parameter and no `[Authorize(Roles=...)]` - scoped internally to the caller's accessible projects the same way `ProjectsController.List` is (see `DashboardService` in [docs/application.md](application.md)) |
| `UsersController` | `/api/users` | `[Authorize(Roles="Admin")]` — roles, status, project assignment |
| `AuditLogController` | `/api/audit-log` | `[Authorize(Roles="Admin")]` — read-only |

## Workflow integration

Pipeline order in `Program.cs`:

```
UseHttpsRedirection → UseExceptionHandler → UseAuthentication → UseAuthorization → MapControllers
```

`UseExceptionHandler` is registered early enough to catch exceptions from every stage after it,
including authentication/authorization failures.

## Authentication

Full pipeline configuration lives in [`Program.cs`](../src/TeamPilot.API/Program.cs):

- `Program.cs` fails fast at startup if `Jwt:SigningKey` is shorter than 32 characters (256
  bits) — the minimum HS256 requires — so a misconfigured key is caught immediately rather than
  silently padded and causing every issued token to fail validation.
- `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` validates
  tokens **we** issued — `ValidIssuer`/`ValidAudience` from `Jwt:Issuer`/`Jwt:Audience`,
  `IssuerSigningKey` from `Jwt:SigningKey`. `MapInboundClaims = false` so claim types
  (`sub`, `name`, `email`, the role claim) stay exactly as `AccessTokenGenerator` issued them —
  `HttpContextCurrentUserContext` relies on this to read `"sub"` directly rather than the
  auto-remapped `ClaimTypes.NameIdentifier`.
- The refresh token is delivered as an `HttpOnly`, `Secure`, `SameSite=Strict` cookie scoped to
  `/api/auth` (`AuthController.SetRefreshTokenCookie`) — never exposed to JavaScript. The
  access token is returned in the JSON response body for the client to hold in memory and send
  as `Authorization: Bearer <token>`.
- `HttpContextCurrentUserContext` ([`Auth/HttpContextCurrentUserContext.cs`](../src/TeamPilot.API/Auth/HttpContextCurrentUserContext.cs))
  implements Application's `ICurrentUserContext` by reading claims off `HttpContext.User`.

### Configuration

| Key | Purpose |
|---|---|
| `Jwt:SigningKey` | Symmetric HS256 key for our issued JWTs (secret — via user-secrets) |
| `Jwt:Issuer` / `Jwt:Audience` | Issued and validated on our own tokens |
| `Jwt:AccessTokenLifetimeMinutes` | Access token lifetime (default 15) |
| `Auth:RefreshTokenLifetimeDays` | Refresh token lifetime (default 14) |
| `Auth:SeedAdminEmails` | Emails auto-granted Admin on first login |
| `Auth:Providers:{Google,Microsoft}:MetadataAddress` | OIDC discovery document URL |
| `Auth:Providers:{Google,Microsoft}:Audience` | Your OAuth client ID with that provider (secret) |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `Llm:*` | Claude API configuration (`ApiKey` is a secret) |
| `Git:DefaultAuthorEmail` | Commit author email used for agent-authored commits |
| `Git:SandboxRoot` | Root folder under which each project's cloned sandbox lives, one subfolder per project id (resolved against the content root when relative) |

## Cross-origin requests (CORS)

Now that the Angular frontend (`src/TeamPilot.UI`) runs on its own origin (`http://localhost:4200`
in development), `Program.cs` registers a named CORS policy (`AngularClient`) and applies it via
`app.UseCors(...)`, placed after `UseHttpsRedirection` and before `UseAuthentication` so
preflight (`OPTIONS`) requests are handled before the auth pipeline runs.

The policy allows only the origins listed in `Cors:AllowedOrigins` (`appsettings.json`; defaults
to `http://localhost:4200` and `https://localhost:4200`) plus any header/method, and calls
`AllowCredentials()`. Credentials must be paired with explicit origins rather than
`AllowAnyOrigin()` — this is a hard browser/CORS-spec requirement, and it matters here because
`POST /api/auth/refresh` and `POST /api/auth/login/{provider}` rely on the browser sending the
`teampilot_rt` cookie cross-origin (`withCredentials: true` client-side). Add the deployed
frontend's origin to `Cors:AllowedOrigins` for any non-dev environment.

## Example

```bash
# 1. Client obtains a Google id_token via Google's own SDK, then:
curl -X POST https://localhost:7085/api/auth/login/google \
  -H "Content-Type: application/json" \
  -d '{"idToken":"<google-id-token>"}' \
  -c cookies.txt

# 2. Use the returned accessToken for subsequent calls:
curl https://localhost:7085/api/auth/me \
  -H "Authorization: Bearer <accessToken>"

# 3. Refresh when the access token expires (refresh token is sent automatically via cookie):
curl -X POST https://localhost:7085/api/auth/refresh -b cookies.txt -c cookies.txt
```

## Error handling

`GlobalExceptionHandler` maps exceptions to a `ProblemDetails` response:

| Exception | HTTP status |
|---|---|
| `Application.Common.Exceptions.ValidationException` | 400 (includes a per-field `errors` dictionary) |
| `Application.Common.Exceptions.AuthenticationFailedException` | 401 |
| `Application.Common.Exceptions.ForbiddenException` | 403 |
| `Application.Common.Exceptions.NotFoundException` | 404 |
| `Application.Common.Exceptions.GitOperationException` | 422 (clone/push/fetch against a project's remote failed - bad URL/token, unreachable host - or, on `POST /api/projects/{projectId}/sprints`/`PUT /api/sprints/{id}`, the requested base branch doesn't exist on the remote) |
| `Application.Common.Exceptions.LlmOperationException` | 422 (a Claude API call failed after retries were exhausted) |
| `Application.Common.Exceptions.WorkflowLockedException` | 409 (a pipeline structure change was attempted while the project has an `InProgress` or `Blocked` ticket) |
| `Application.Common.Exceptions.AgentInstructionsIncompleteException` | 409 (an agent needs Constitution/Guideline/Requirement instructions before it can join a workflow) |
| `Application.Common.Exceptions.InvalidWorkflowOperationException` | 409 (a workflow change is invalid given the rest of the project's stage sequence) |
| `Application.Common.Exceptions.StaleConflictResolutionException` | 409 (approving a ticket found a resolved conflict prepared against a base-branch tip that's since moved further - the affected conflict(s) are reset to `Detected` before this is returned) |
| `Application.Common.Exceptions.ProjectNotReadyException` | 409 (creating a ticket against a project that's still `Cloning`, or whose clone `Failed`) |
| `Application.Common.Exceptions.ProjectHasActiveTicketsException` | 409 (removing a project while any of its sprints has a ticket `InProgress` or `ForReview`) |
| `Application.Common.Exceptions.SprintHasActiveTicketsException` | 409 (removing a sprint that has a ticket `InProgress` or `ForReview`) |
| `Application.Common.Exceptions.TicketNotAssignedToSprintException` | 409 (starting the pipeline or linking a branch on a backlog ticket - assign it to a sprint first) |
| `Application.Common.Exceptions.TicketHasNoLinkedBranchException` | 409 (approving a ticket that has no linked branch to merge) |
| `Domain.Exceptions.DomainException` (any subtype, incl. `TicketAlreadyAssignedToSprintException`) | 409 |
| anything else | 500 (message is generic; the real exception is logged, never returned to the client) |

## Future considerations

- **Rate limiting**: none yet, particularly relevant for `/api/auth/login/{provider}` and
  `/api/auth/refresh`.
- **API versioning**: the API has no version prefix; fine for a single first-party client, may
  need revisiting if external consumers appear.
- **Generated TypeScript client**: the Angular frontend (`src/TeamPilot.UI`) currently uses
  hand-written TypeScript interfaces and enum string-unions mirroring each DTO/enum by hand
  (see `src/TeamPilot.UI/src/app/core/models`) rather than a client generated from the Swagger
  document. This was a deliberate scope choice for the initial frontend build, not a rejection
  of the idea — generating a typed client would remove the manual-sync burden and is still worth
  adopting if the DTO surface keeps growing.
