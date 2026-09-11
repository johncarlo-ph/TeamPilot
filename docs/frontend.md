# Frontend (`TeamPilot.UI`)

[← Back to README](../README.md)

## Purpose

`TeamPilot.UI` (`src/TeamPilot.UI`) is the Angular 21 single-page application that consumes
`TeamPilot.API` over HTTP: the Kanban ticket board, ticket detail (diffs, reviews, conflict
resolution), and the admin surfaces (agents/instructions, users, audit log). It is a separate
deployable artifact and origin from the API — see
[Cross-origin requests (CORS)](api.md#cross-origin-requests-cors) for how the two are wired
together.

Its role is purely a Presentation-layer client: it has no server-side logic of its own and
enforces nothing the API doesn't already enforce independently (role gating in the UI is a UX
convenience, not a security boundary — see "Authorization" below).

## Architectural decisions

**Standalone components and signals throughout, no NgRx.** Per
[constitution/Angular-20-UI-Development-Guidelines.MD](../constitution/Angular-20-UI-Development-Guidelines.MD),
every component is standalone (no `NgModule`s) and local/UI state is held in signals
(`signal()`/`computed()`) rather than a global store. RxJS is used where it's the right tool —
HTTP calls, the polling intervals, interceptor pipelines — not as a substitute for local state.

**Hand-written models mirror the API's DTOs and enums exactly**
(`src/app/core/models/*.model.ts`, `enums.ts`), rather than a generated client. Every enum is a
TypeScript string-literal union with the exact C# member names, matching the API's global
`JsonStringEnumConverter` (enums serialize as strings, never integers). This was a deliberate
scope choice for the initial build — see [docs/api.md](api.md#future-considerations) for the
trade-off.

**Auth token handling matches the API's split by design.** The access token is held in memory
only (a signal inside `AuthService`, never `localStorage`/`sessionStorage`) and attached as a
bearer header by `authInterceptor`; the refresh token is the API's `HttpOnly` cookie, which the
browser sends automatically and the app never reads. `withCredentials: true` is set only on the
three `/api/auth/*` calls that actually touch that cookie (login, refresh, logout) — not
globally — so it doesn't leak onto unrelated requests. On app bootstrap,
`provideAppInitializer` calls `AuthService.tryRestoreSession()` (a silent `/api/auth/refresh`)
so a page reload doesn't bounce an already-logged-in user to `/login`; on a 401 from any other
endpoint, `authInterceptor` attempts one silent refresh-and-retry before giving up and
redirecting to `/login`.

**Google via Identity Services, Microsoft via MSAL — genuinely different flows.** Google sign-in
uses `google.accounts.id.initialize()`/`renderButton()` (Google Identity Services), which hands
back an `id_token` directly through a JS callback — no redirect URI is involved, only
"Authorized JavaScript origins" in Google Cloud Console. Microsoft sign-in uses
`@azure/msal-browser`'s `loginPopup()`, which *does* need a redirect URI (the popup navigates to
Microsoft and back before MSAL intercepts the response) — registered in Azure AD as a
**Single-page application** platform redirect URI, set explicitly in code to
`${window.location.origin}/login` (not left to MSAL's default of `window.location.href`, which
would vary with query params like `?returnUrl=...` and risk not matching what's registered).
Both providers hand their raw `id_token` to the same
`POST /api/auth/login/{provider}` — the API does the actual verification against each provider's
JWKS (see [docs/infrastructure.md](infrastructure.md#authentication)).

**Polling instead of SSE.** The README's original design called for real-time updates via
Server-Sent Events, but no SSE/WebSocket/SignalR endpoint exists anywhere in the backend today.
Rather than build new backend real-time infrastructure as part of a frontend task, the board
(`features/board`) and ticket detail (`features/ticket-detail`) pages poll their respective
GET endpoints on a short interval (8s / 10s) via `interval(...).pipe(switchMap(...))`, scoped to
the active route with `takeUntilDestroyed()`. Revisit this if/when the backend grows a
real-time transport — see [docs/cross-cutting-concerns.md](cross-cutting-concerns.md).

**Drag-and-drop is mapped to the API's actual transition endpoints, not a generic status
setter.** There is no `PUT /tickets/{id}/status`; a ticket only moves between columns through
specific actions (`assign-agents`, `move-to-review`, submitting a `Review`). The board
(`features/board/board.ts`) uses Angular CDK drag-and-drop purely as the *gesture* — dropping a
card on a column looks up the `(from, to)` pair and either calls the matching endpoint directly
(`InProgress → ForReview`) or opens the matching dialog first (`ToDo → InProgress` opens
"Assign Agents"; `ForReview → Done`/`ForReview → InProgress` opens the review form pre-set to
Approve/RequestChanges). An unsupported drop (e.g. `ToDo → Done`) is rejected client-side with a
toast rather than attempting a call that doesn't exist.

**Ticket cards intentionally omit "PR."** The original mockup's card design showed
branch/PR/commits/agent-status, but the API models only `Ticket.BranchName` and a separate
`Commit` list (only present on `TicketDetailDto`, not the list-view `TicketDto`) — there is no
pull-request entity. The board list shows only what the list endpoint actually returns (title,
branch, updated date) rather than either fabricating a PR field or fetching full ticket detail
per card (which would turn an O(1) list load into an N+1 fan-out, against the "avoid
overfetching" guideline). Commits and agent-assignment status are shown on the ticket detail
page, which does load the full aggregate.

**Self-built diff renderer, no diff library dependency.** `shared/components/diff-viewer`
parses the unified-diff text the API already returns (`Commit.DiffContent`, `GitFileDiff.Patch`,
`Conflict.ConflictingDiffContent`) line-by-line into add/remove/hunk/header/context spans. This
was small enough to not justify an extra npm dependency.

## Authorization (client-side mirror, not enforcement)

`authGuard` and `adminGuard` (functional `CanActivateFn`s) gate routes, and `AuthService.canApprove()`
hides the Approve option for Analysts in the review form — but every one of these mirrors a rule
the API already enforces server-side (`IProjectAccessGuard`, `[Authorize(Roles=...)]`, the
Approve-role check in `ApprovalGateService`; see [docs/application.md](application.md)). None of
this is a security boundary on its own — a client-side check only improves UX by not showing a
control the server would reject anyway.

One additional client-side-only guard exists for a real backend gap found during development:
approving a ticket with no linked branch currently throws an unhandled `InvalidOperationException`
in `ApprovalGateService` (a raw 500, not a proper validation error) — `review-form.ts`'s
`hasLinkedBranch` input disables the Approve option before that request can even be sent. The
underlying fix still belongs server-side (return a typed, mapped exception); this is tracked as
follow-up work, not fixed as part of this frontend change.

## Code style notes

- Folder layout: `core/` (models, HTTP services, interceptors, guards, notifications — one
  singleton service per API resource, e.g. `TicketsService`, `AgentsService`), `layout/` (the
  authenticated shell: navbar + `<router-outlet>` + toast stack), `features/*` (one folder per
  page/route), `shared/components` (cross-feature reusable UI: `Modal`, `DiffViewer`,
  `StatusBadge`).
- Every HTTP service is a thin, one-method-per-endpoint wrapper (no generic `ApiClient<T>`) —
  mirrors the API's own per-aggregate-repository philosophy
  (see [docs/application.md](application.md)).
- Forms are typed Reactive Forms (`FormBuilder.nonNullable.group({...})`) everywhere a form
  exists — no template-driven forms except a couple of ad-hoc filter inputs (`FormsModule`
  `[(ngModel)]`) where a full `FormGroup` would be overkill (e.g. the git-diff branch pickers).
- Reusable dialogs (`create-ticket-form`, `assign-agents-modal`, `review-form`,
  `project-form`, `agent-form`, `user-edit-modal`) all wrap the shared `Modal` component and
  follow the same `open` input / `closed` output / `<action>` output contract.
- New Angular control-flow syntax (`@if`/`@for`/`@switch`) is used throughout; no `*ngIf`/`*ngFor`.

## Configuration

| File | Purpose |
|---|---|
| `src/environments/environment.ts` / `environment.development.ts` | `apiBaseUrl`, `auth.googleClientId`, `auth.microsoft.{clientId,authority}` — swapped by the `production`/`development` build configurations (`fileReplacements` in `angular.json`) |
| `.claude/launch.json` (repo root) | Dev-server launch config (`npm run start --prefix src/TeamPilot.UI`, port 4200) used by this repo's tooling |

`googleClientId`/`microsoft.clientId` must match `Auth:Providers:{Google,Microsoft}:Audience` in
the API's own configuration (see [docs/api.md](api.md#configuration)) — same OAuth client
registration, referenced from both sides.

## Workflow integration

```bash
cd src/TeamPilot.UI
npm install
npm start          # ng serve, http://localhost:4200
npm run build      # production build -> dist/TeamPilot.UI
npm test           # ng test (Vitest-based unit-test builder)
```

Requires the API reachable at `environment.apiBaseUrl` (`https://localhost:7085/api` by default)
with `Cors:AllowedOrigins` including the frontend's own origin — see
[docs/api.md](api.md#cross-origin-requests-cors).

## Example

```ts
// board.ts — a drag from "ForReview" to "Done" opens the review dialog pre-set to Approve,
// it does not call a generic status-update endpoint:
if (ticket.status === 'ForReview' && targetStatus === 'Done') {
  this.reviewInitialDecision.set('Approve');
  this.reviewTarget.set(ticket);
  return;
}
```

## Error handling

`errorInterceptor` reads the API's `ProblemDetails` response (`title`/`detail`/`errors`) and
surfaces a user-facing message via `NotificationService` (a toast), falling back to a
status-code-specific generic message (403/404/409/0/other) when the body doesn't carry field
errors. Auth endpoints (`/auth/*`) are excluded from this interceptor — `AuthService`/`Login`
handle their own error states directly (e.g. a failed sign-in shows an inline message rather
than a toast).

## Future considerations

- **End-to-end tests** (login → create ticket → assign agent → review → approve, through the
  actual UI) — not implemented yet; today's frontend test coverage is a small representative
  unit-test sample (`AuthService`, `StatusBadge`), not full-flow coverage. See
  [docs/cross-cutting-concerns.md](cross-cutting-concerns.md#testing-strategy).
- **Generated TypeScript client** from the Swagger document, to remove the manual-sync burden
  of hand-written models — see [docs/api.md](api.md#future-considerations).
- **Real-time updates**: replace polling with SSE/WebSockets/SignalR once the backend grows a
  real-time transport.
- **Server-side fix for the branchless-approve 500** described above under "Authorization" —
  the client-side guard is a stopgap, not a substitute for the API returning a proper error.
- **No delete endpoints** exist for `Project`/`Ticket`/`Agent` on the API, so the UI has no
  delete affordance for any of them either — test/demo data created through the UI can't be
  removed without going directly to the database.
