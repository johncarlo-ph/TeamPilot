# Frontend (`TeamPilot.UI`)

[← Back to README](../README.md)

## Purpose

`TeamPilot.UI` (`src/TeamPilot.UI`) is the Angular 21 single-page application that consumes
`TeamPilot.API` over HTTP: the Kanban ticket board, ticket detail (diffs, reviews, conflict
resolution), and the admin surfaces (agent pipeline/instructions, users, audit log). It is a separate
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
specific actions (`start`, `move-to-review`, submitting a `Review`). The board
(`features/board/board.ts`) uses Angular CDK drag-and-drop purely as the *gesture* — dropping a
card on a column looks up the `(from, to)` pair and calls the matching endpoint directly, with no
dialog in between (`ToDo → InProgress` calls `POST /tickets/{id}/start`, which runs the whole
Research→Design→Coding→Testing pipeline and returns the final ticket state — the card may show up
in "For Review" once polling picks up the result, since the pipeline runs all the way through in
that one call; `InProgress → ForReview` calls `move-to-review` directly; `ForReview → Done`/
`ForReview → InProgress` opens the review form pre-set to Approve/RequestChanges). An unsupported
drop (e.g. `ToDo → Done`) is rejected client-side with a toast rather than attempting a call that
doesn't exist. `features/ticket-detail` renders the same "start"-triggering button (labeled
"Start" or "Run Pipeline" depending on ticket status) for the case where a human wants to
(re-)trigger the pipeline without going through the board — e.g. retrying after a failed run, or
after a review's `RequestChanges` sent the ticket back to In Progress.

**Cancelling a ticket is a detail-page button, not a board drop target.** `Cancelled` is a real
`TicketStatus` value but deliberately isn't one of the 4 `BOARD_COLUMNS` — "give up on this
ticket" doesn't fit the drag-a-card-between-columns metaphor the way `start`/`move-to-review`/
`review` do, and adding a 5th column would clutter the fixed Research→Design→Coding→Testing
view. Instead, `features/ticket-detail` shows a **Cancel Ticket** button (for `ToDo`/
`InProgress`/`ForReview` tickets) that uses the same `confirm()`/`prompt()` pattern as deleting an
instruction template — a plain browser confirm, then an optional reason — rather than a dedicated
modal. `board.ts`'s `ticketsByStatus` grouping filters `Cancelled` tickets out entirely, so a
cancelled ticket simply disappears from the board; it's still reachable directly by URL.

**Linking a branch confirms before acting, because the same button means two different things.**
`features/ticket-detail/branch-panel` calls `GET /api/git/branches/exists` when "Link Branch" is
clicked, then opens a confirmation modal worded for whichever case came back — "an existing
branch was found, link this ticket to it?" vs. "no branch exists yet, a new one will be created"
— before actually calling `POST /api/git/branches`. This avoids silently reusing someone else's
in-progress branch (or silently creating an unexpected one) under a single ambiguous button label.

**Deleting a branch is the one real "delete" in this UI, so it gets the same `confirm()`
treatment as deleting an instruction template — no dedicated modal.** `branch-panel`'s **Delete
Branch** button only renders once the ticket is `Cancelled` (the API would reject it otherwise),
calls `DELETE /api/git/branches/{ticketId}`, and emits the same `changed` output the "Link
Branch" flow does so `ticket-detail` knows to refresh. That output used to be called
`branchCreated`; it was renamed to `changed` when this button was added, since it now covers two
different mutations, not one.

**The board is a two-panel layout: Live Agent chat on the left, the kanban board on the right.**
`board.html` wraps both in one `.row g-3` (`.col-12 col-lg-4` / `.col-12 col-lg-8`), the same
Bootstrap grid split `ticket-detail.html` already used for its main-content/side-panel layout -
no new layout primitive introduced. `features/board/chat-panel` (`ChatPanel`) owns its own state
entirely: it takes only `projectId` as input, loads the project's chat history itself via
`LiveAgentChatService.listMessages` on init (an `effect()` reacting to the `projectId` signal
input, not a poll - the chat only changes in response to a message this component itself sent),
and appends the user's message optimistically before the `POST` resolves. An assistant message
carrying `proposedTicketTitle`/`proposedTicketDescription` renders as an approval card with a
"Create ticket" button right in the chat thread - clicking it calls the ordinary
`TicketsService.create(...)` (the same one `CreateTicketForm` uses), and the newly created ticket
simply shows up on the board once the existing 8s ticket poll (above) picks it up - no new
refresh plumbing was added for this. `ChatPanel` never calls a "confirm draft" endpoint because
there isn't one; approving a draft and creating a ticket are the same API call.

**The board's project name, column colors/icons, and column set are two different concerns kept
separate on purpose.** The header shows `project().name` (loaded the same way `ticket-detail`
loads its project — a separate subscription alongside the tickets poll) so a board reached from a
bookmark or a shared link is unambiguous about which project it belongs to; `BOARD_COLUMNS` in
`board.ts` carries a per-status `icon` and `accentClass` (⏳/🔧/👀/✅, one accent color each) purely
for visual scannability of the 4 fixed pipeline stages. `status-badge.ts`'s ticket-status badge
colors (`ToDo`/`InProgress`/`ForReview`/`Done`, used in ticket detail, reviews, etc.) intentionally
reuse this same accent palette — `text-bg-primary`/`text-bg-warning`/`text-bg-success` are the
same colors as `$board-todo-color`/`$board-inprogress-color`/`$board-done-color` because those
Bootstrap variables are literally aliased to `$primary`/`$warning`/`$success`; `ForReview`'s purple
has no built-in Bootstrap variant, so it gets its own `.text-bg-forreview` class in
`styles.scss` set to `$board-forreview-color`. So a ticket's badge always matches the column it
sits in, wherever that badge is shown. The custom CSS for both (`.board-column--*`, `.board-column-title`,
`.text-bg-forreview`, `.ticket-card` hover) lives in the single global `src/styles.scss`, not per-component
`styleUrls` - this project has never used scoped component styles (everything else is Bootstrap
utility classes in the template), so a new per-component stylesheet would be a second, competing
styling convention rather than a small addition to the existing one.

**Branch names are links everywhere except the board card.** There is no backend "branch URL"
field — `core/utils/git-url.util.ts`'s `buildBranchUrl(remoteUrl, branchName)` strips `.git` and
appends `/tree/{branch}` (the GitHub/GitLab convention; matches the two providers `IGitService`
supports). `branch-panel`'s "Linked branch" line and the ticket-detail commit list both use it
and need the owning project's `remoteUrl` passed in alongside the ticket/commit, since
`TicketDto`/`CommitDto` carry only the branch name. `ticket-card` deliberately does **not** turn
the branch name into a link, even though it shows one: the whole card is a Bootstrap
`stretched-link` (the title `<a>` gets `.stretched-link`, which covers the entire `position-relative`
ancestor), and a second real `<a>` layered on top of a `stretched-link` needs its own elevated
`z-index` to stay clickable — which used to leave the branch-name line "in front of" the
stretched overlay but not actually a link, so hovering it looked interactive but silently ate the
click instead of navigating to the ticket. Rather than build a second competing click target on
a card, the branch name is now plain text with no positioning tricks, and the whole card
(including that line) navigates to `/tickets/{id}`; the branch is only ever a live link once
you're on the ticket detail page.

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

**Instruction templates are a global admin page, not project-scoped.** `features/admin/
instruction-templates/` follows the same shape as `features/admin/users/` (a list + a
create/edit modal, route gated by `adminGuard`, nav link only shown when
`authService.isAdmin()`) but isn't nested under a project route, since `InstructionTemplate`
belongs to no project. The one place it's consumed outside its own admin page is
`features/agents/instruction-editor/instruction-editor.ts`, which now takes an `agentRole` input
(the agents list already had this — it just wasn't being looked up and passed down before) and
fetches templates filtered to that role, one **Template** dropdown per instruction type. The
dropdown is always rendered and always enabled (never disabled, even with zero matching
templates) - the placeholder option's label switches between "Populate from template..." and
"No templates for this role/type yet" so an empty dropdown doesn't read as broken. Selecting a template fills the
matching textarea (`form.get(type).setValue(...)`) and the `<select>` keeps showing the picked
option, tracked in a `selectedTemplateIds` signal keyed by instruction type — it's still just a
content fill, not a persisted association (the selection resets on save/reload); the existing
"Save & Ingest" flow is what actually commits anything, same as typing the content by hand.

**`features/agents/agents.ts` shows the project's ordered workflow, not a static 4-item list.**
It reads `WorkflowService.list()` (ordered `WorkflowStageDto[]`, each carrying its `AgentDto`) and
`listUnscheduledAgents()` instead of `AgentsService.listForProject()` directly - the latter is
still used elsewhere for `GET /agents/{id}` reads, but stage placement/order now lives in
`Workflow/`, mirroring the API split between `AgentsController` and `WorkflowController` (see
[docs/api.md](api.md)). Every mutation (reorder, add-to-pipeline, remove, loop-back) is
admin-only, gated the same `authService.isAdmin()` way as elsewhere in this doc - a non-admin
still sees the full ordered list and can still select a stage to view/edit its instructions
(matching the API, which leaves instruction editing open to any project member), just without the
drag handle, Remove button, or loop-back editor rendered at all. Reordering reuses this codebase's
one existing drag-and-drop precedent - `@angular/cdk/drag-drop`'s `cdkDropList`/`cdkDrag`
(`DragDropModule`), already used by the Kanban board (`features/board/board.html`) - rather than
introducing a second reordering mechanism; a drop calls `moveItemInArray` to compute the requested
order client-side, then `WorkflowService.reorder()` persists it and the response (not the
optimistic local array) is what actually gets rendered, so a rejected reorder (e.g. it would
invalidate a loop-back) settles back to the server's real order once `errorInterceptor` surfaces
the 409's message as a toast. A stage row's "Remove" and the "Pipeline is locked..." banner text
use the browser's native `confirm(...)`/a plain `@if` respectively, following the same
lightweight pattern as `branch-panel.ts`'s delete-branch confirmation rather than introducing a
dedicated confirmation dialog component. **Adding a custom agent is two steps in the UI**,
matching `WorkflowService.CreateCustomAgentAsync`/`AddExistingAgentAsync` being two separate calls
on the backend: the "Add Custom Agent" modal only creates the (blank, unscheduled) agent and
immediately selects it so its instruction editor opens; a separate "Add to pipeline" button (shown
under "Available agents") is what actually schedules it, and is left enabled even though it can
fail server-side if the three instruction types aren't filled in yet - the resulting 409's message
is descriptive enough on its own (surfaced by `errorInterceptor`) that a client-side "is this agent
ready" check wasn't worth adding to `AgentDto` just for this one button's disabled state. Each
row under "Available agents" also gets its own Remove button when `agent.role === 'Custom'`
(`Agents.deleteCustomAgent`, calling `WorkflowService.deleteCustomAgent`) - a plain client-side
role check is enough here, since the backend independently re-validates role, schedule state, and
assignment history before actually deleting anything (see [docs/application.md](application.md)).
It isn't disabled by the `locked()` pipeline banner, matching the backend: an unscheduled agent
can't affect a running ticket either way.

**Every action button shows its own loading state and is disabled for the duration of its
request.** Any button that triggers an HTTP call owns a `signal(false)` flag (e.g. `saving`,
`deleting`, `starting`) set to `true` right before the `subscribe(...)` call and reset in both
the `next` and `error` callbacks, bound to that button's `[disabled]` and, for most, swapped into
its label (e.g. `"Save"` → `"Saving..."`) so a slow request can't be double-submitted and the user
always sees it's in flight. Where a form or action is reused across multiple items in a list
(conflict resolution actions, workflow-stage Remove/loop-back, pipeline-run Start/Complete), the
flag is a `ReadonlySet<string>` of in-flight ids instead of one boolean, so only the row actually
being mutated disables — the rest of the list stays interactive. Where the button lives in a
reusable child component (`ReviewForm`, `CreateTicketForm`, `ProjectForm`,
`InstructionTemplateForm`) and the actual API call is made by the parent that owns the
`(submitted)`/`(saved)`/`(created)` output, the flag is threaded down as a `saving`/`submitting`/
`creating` input instead of living in the child, since the child has no way to know when the
parent's request resolves. This is why forms without their own dedicated inline error UI (see
"No inline server-error handling" below) still gained an `error` callback: not to show a message,
but to reset the loading flag so a failed request doesn't leave the button stuck disabled.
Purely local actions (opening/closing a modal, toggling a signal, drag-and-drop reordering) are
unaffected — this convention only applies to buttons that call into the HTTP layer.

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
  authenticated shell: navbar + `<router-outlet>` + toast stack — the navbar is `.sticky-top` so
  the Projects/Users/Audit Log links stay reachable on a long scrolled page), `features/*` (one
  folder per page/route), `shared/components` (cross-feature reusable UI: `Modal`, `DiffViewer`,
  `StatusBadge`).
- Every HTTP service is a thin, one-method-per-endpoint wrapper (no generic `ApiClient<T>`) —
  mirrors the API's own per-aggregate-repository philosophy
  (see [docs/application.md](application.md)).
- Forms are typed Reactive Forms (`FormBuilder.nonNullable.group({...})`) everywhere a form
  exists — no template-driven forms except a couple of ad-hoc filter inputs (`FormsModule`
  `[(ngModel)]`) where a full `FormGroup` would be overkill (e.g. the git-diff branch pickers).
- Reusable dialogs (`create-ticket-form`, `review-form`, `project-form`, `user-edit-modal`) all
  wrap the shared `Modal` component and follow the same `open` input / `closed` output /
  `<action>` output contract, including the "Add Custom Agent" dialog on `features/agents/agents.ts`
  (name only - the agent starts with no instructions, per `WorkflowController`, see
  [docs/api.md](api.md)).
- `project-form`'s Access Token field (`type="password"`) is the first masked input in this
  codebase - no prior precedent existed to follow. It's required when creating a project and
  optional when editing (blank = keep the currently stored token); the validator is
  added/cleared on the `accessToken` control inside the same `effect()` that already resets the
  form per the `project()` input. Remote URL is rendered as read-only text instead of a form
  control when editing, since `Project.RemoteUrl` is immutable after creation.
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
- **No delete endpoints** exist for `Project` or `Ticket` on the API, so the UI has no delete
  affordance for either. `Agent` has two narrow exceptions, both on the agents page: the
  pipeline's "Remove" button calls `DELETE /api/projects/{projectId}/workflow/stages/{stageId}`,
  which only removes a stage from the sequence (the agent and its instruction history stay
  reachable under "Available agents"); and the "Available agents" section's own "Remove" button
  on a `Custom`-role agent calls `DELETE /api/projects/{projectId}/workflow/agents/{agentId}`,
  which really does permanently delete it - only shown for `Custom` agents, since that's the only
  role the API will actually let you delete (see [docs/domain.md](domain.md)).
- **No inline server-error handling on any form**, `project-form` included: every
  button-triggered request's `error` callback only resets that button's loading signal (see
  "Every action button shows its own loading state" above) — a failed create (e.g. an
  unreachable remote or bad access token, surfaced by the API as 422) still only shows the
  generic `errorInterceptor` toast, and the modal stays open with whatever was typed, rather than
  a field-level inline message. Consistent with every other form today, not a regression specific
  to this one.
