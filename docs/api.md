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
  (`POST /api/projects/{projectId}/tickets`), flat routes for single-resource operations where
  the id is already globally unique (`GET /api/tickets/{id}`).
- Enums serialize as their string names (`JsonStringEnumConverter`, registered globally, plus a
  Swagger `EnumSchemaFilter` so the generated schema matches) — never raw integers.
- XML doc comments are enabled (`GenerateDocumentationFile`) and feed Swagger's
  `IncludeXmlComments`; `CS1591` (missing XML comment) is suppressed rather than forcing every
  public member to have one.

## Controllers

| Controller | Base route | Notes |
|---|---|---|
| `AuthController` | `/api/auth` | `[AllowAnonymous]` login/refresh/logout; `GET /me` requires auth but no specific role |
| `ProjectsController` | `/api/projects` | `Create`/`Update` are `[Authorize(Roles="Admin")]`; `List`/`GetById` are project-scoped in the service. `Create` synchronously clones the submitted `RemoteUrl` before returning - can be slow for large repos, and returns 422 (`GitOperationException`) if the clone fails |
| `TicketsController` | `/api/projects/{projectId}/tickets`, `/api/tickets/{id}/...` | Ticket board CRUD; `POST /tickets/{id}/start` runs the ticket through the project's configured agent workflow (`OrchestrationService.RunPipelineAsync`, default Research→Design→Coding→Testing) in one call, returning `Blocked`/`BlockingQuestionId` on the result if a stage asked a question or a Git/LLM failure occurred instead of reaching For Review; `POST /tickets/{id}/cancel` abandons a pre-merge ticket (`ToDo`/`InProgress`/`ForReview`/`Blocked`) with an optional reason, terminal, no Git side effects; `POST /tickets/{id}/retry` unblocks a ticket blocked by a Git/LLM failure and re-runs the pipeline (409 if it's blocked by a clarifying question instead - use `TicketQuestionsController` for that) |
| `AgentsController` | `/api/projects/{projectId}/agents`, `/api/agents/{id}` | Read/update only — list, get, edit configuration/status. No `POST` here: the 4 default pipeline agents and the standing `LiveAgent` are system-provisioned per project; a custom agent is created through `WorkflowController` instead |
| `WorkflowController` | `/api/projects/{projectId}/workflow` | The project's admin-configurable agent workflow. `GET` (stages) and `GET /unscheduled-agents` are open to any authenticated user with project access; `POST /agents` (blank custom agent, not yet scheduled), `POST /stages` (schedule an unscheduled agent - rejects incomplete instructions), `DELETE /stages/{id}`, `PUT /order`, and both loop-back endpoints (`PUT`/`DELETE .../loop-back`) are `[Authorize(Roles="Admin")]` and 409 while the project has an `InProgress` ticket; `DELETE /agents/{agentId}` (also Admin-only) permanently deletes a custom agent, but only if it's unscheduled and has never been assigned to a ticket - not subject to the `InProgress` lock, since an unscheduled agent can't affect a running ticket |
| `InstructionsController` | `/api/agents/{agentId}/instructions` | Versioned instructions |
| `LiveAgentChatController` | `/api/projects/{projectId}/live-agent/messages` | `GET` returns the project's chat history (oldest first); `POST { content }` sends a user message, runs the Live Agent's bounded tool-use loop, and returns its assistant reply - including `proposedTicketTitle`/`proposedTicketDescription` when the model drafted a ticket. Creating the actual ticket from a draft is not a separate endpoint - the UI calls the ordinary `POST /api/projects/{projectId}/tickets` once the user approves it |
| `InstructionTemplatesController` | `/api/instruction-templates` | Global, not project-scoped. `GET`/`GET/{id}` open to any authenticated user; `POST`/`PUT/{id}`/`DELETE/{id}` are `[Authorize(Roles="Admin")]` per-action |
| `GitController` | `/api/git/branches`, `/api/git/branches/{ticketId}`, `/api/git/branches/exists`, `/api/projects/{projectId}/git/{diff,conflicts}` | `POST /branches`, `DELETE /branches/{ticketId}`, and `GET /branches/exists` delegate to `ITicketService` (which enforces project access internally); `diff`/`conflicts` are thin `IGitService` pass-throughs with an explicit `IProjectAccessGuard` check. `POST /branches` returns 409 (`BranchAlreadyLinkedException`) if the branch name is already linked to a different ticket in the same project; `DELETE /branches/{ticketId}` returns 409 (`InvalidTicketStateTransitionException`) unless the ticket is `Cancelled` |
| `ReviewsController` | `/api/tickets/{ticketId}/reviews` | Approval-gate submissions |
| `TicketQuestionsController` | `/api/tickets/{ticketId}/questions` | `GET` lists a ticket's clarifying-question/failure history (reads `ITicketQuestionRepository` directly, same pattern as `ReviewsController`'s `GET`); `POST /{questionId}/answer` answers the pending question, unblocks the ticket, and re-runs the pipeline |
| `ConflictsController` | `/api/tickets/{ticketId}/conflicts`, `/api/conflicts/{id}/...` | Conflict detection/resolution |
| `PipelineRunsController` | `/api/projects/{projectId}/pipeline-runs`, `/api/pipeline-runs/{id}/...` | CI/CD status tracking |
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
| `Application.Common.Exceptions.GitOperationException` | 422 (clone/push/fetch against a project's remote failed - bad URL/token, unreachable host) |
| `Application.Common.Exceptions.LlmOperationException` | 422 (a Claude API call failed after retries were exhausted) |
| `Application.Common.Exceptions.WorkflowLockedException` | 409 (a pipeline structure change was attempted while the project has an `InProgress` or `Blocked` ticket) |
| `Application.Common.Exceptions.AgentInstructionsIncompleteException` | 409 (an agent needs Constitution/Guideline/Requirement instructions before it can join a workflow) |
| `Application.Common.Exceptions.InvalidWorkflowOperationException` | 409 (a workflow change is invalid given the rest of the project's stage sequence) |
| `Domain.Exceptions.DomainException` (any subtype) | 409 |
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
