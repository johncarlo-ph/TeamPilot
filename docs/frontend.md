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

**SSE-driven refresh, not polling.** The README's original design called for real-time updates
via Server-Sent Events; the board (`features/board`) and ticket detail (`features/ticket-detail`)
pages used to poll their respective GET endpoints on a short interval (8s/10s) instead, since no
real-time transport existed in the backend. That transport now exists (`IProjectEventBroadcaster`,
`GET /api/projects/{projectId}/events` — see [docs/application.md](application.md) and
[docs/api.md](api.md)), and both pages consume it via `core/services/project-events.service.ts`'s
`ProjectEventsService.stream(projectId)` in place of the old `interval(...)`. A 60s `interval` is
still merged in alongside it as a safety net for a stuck/misbehaving connection, but it's no
longer the primary refresh mechanism.

`ProjectEventsService` deliberately consumes the SSE endpoint via `fetch()` + `ReadableStream`
rather than native `EventSource`: `EventSource` can't set a custom `Authorization` header, only a
cookie or a query-string token, and this app holds its access token in memory and attaches it as
a header (see "Auth token handling" above) — reusing that avoids a second, token-in-URL auth
pattern with no other precedent in this codebase. The trade-off is that `fetch` gives none of
`EventSource`'s built-in reconnect behavior, so the service reconnects itself with a capped
exponential backoff (1s → 2s → 5s → 10s, reset on a successful message) on any stream error or
clean close, and never errors its returned `Observable` — it's meant to be merged in once and
left running for the page's lifetime, the same way `interval(...)` used to be.

Events carry no ticket/question state of their own — just `{ type: 'TicketChanged' |
'TicketQuestionChanged' | 'TicketAgentEventLogged' | 'ProjectCloneProgress', projectId, ticketId,
occurredAtUtc, cloneProgress }` (`ProjectEventDto`, `core/models/project-event.model.ts`). Both
pages react to one by re-issuing the exact same GET(s) they used to poll with, rather than trying
to apply the event's payload directly — this is why the event shape never needs to be kept in sync
with `TicketDto`/`TicketQuestionDto` the way a full state-push design would. `board.ts` filters the
stream to `TicketChanged` only; `ticket-detail.ts` reacts to both types, since a
`TicketQuestionChanged` event (a new blocking question, or one just answered) needs the same
`forkJoin({ ticket, questions })` refetch as a plain ticket change. `ticket-detail.ts` doesn't know
its ticket's `projectId` until the first `GET /tickets/{id}` resolves (the route only carries the
ticket id), so it fetches the ticket once up front purely to learn which project's event stream to
open, then starts the merged refresh pipeline (which immediately re-fetches both ticket and
questions again via `startWith(0)`) — one extra GET on initial page load, traded for not needing to
thread `projectId` through the route.

**`ProjectCloneProgress` is the one event type with a real payload** (`cloneProgress`, a
`CloneProgressPayload`), rather than being a bare refetch signal like every other type — see
"Project list" below and `ProjectEvent`'s own doc comment in `docs/application.md` for why this one
case deliberately breaks from the rest of the design.

Both pages also `merge` a private `Subject<void>` ("refresh trigger") into the merged
stream/interval before the outer `switchMap`, and every local mutation (create ticket, start
pipeline, move to review, submit a review, etc.) calls it after updating local state. Without
this, a fetch already in flight when the mutation fires can resolve afterward with pre-mutation
data and overwrite the optimistic update — the `switchMap` on the merged stream cancels that stale
request instead, so the next tick is always a fresh, authoritative fetch. `ticket-detail.ts`'s
private `refresh()` helper is just a call to this trigger.

**Submitting a `RequestChanges` review returns fast, not once the pipeline finishes.**
`ApprovalGateService` now kicks the pipeline re-run off in the background instead of awaiting it
(see [docs/application.md](application.md)), so the submit button no longer sits on "Submitting…"
for however long a full re-run takes — the response comes back as soon as the review and status
change are persisted, with the ticket already `InProgress`. Both `board.ts`'s `confirmReview` and
`ticket-detail.ts`'s `submitReview` check `request.decision === 'RequestChanges'` on success and
show a distinct toast ("Review submitted - the pipeline is now running in the background.")
instead of the plain "Review submitted." used for every other decision, so it's clear more work
is still happening after the modal closes. If that background run later fails unexpectedly, the
ticket lands in the `Blocked` column with a retryable `Failure` question — the same UI (and the
same Retry button) already used for a mid-pipeline Git/LLM failure, not a separate error surface.

**The other three ways to (re-)start the pipeline — the `ToDo` → `InProgress` drag/Start button,
answering a clarifying question, and retrying a failure — return just as fast, for the same
reason.** `TicketsService.startPipeline`/`TicketQuestionsService.answer`/`.retry` all now resolve
to a plain `TicketDto` instead of the old `TicketPipelineResultDto` (which carried the finished
run's `testingPassed`/`testingAttempts`/`steps`) — the server kicks the run off detached (see
[docs/application.md](application.md)) and returns before it finishes, so there's no final
verdict to report yet. `board.ts`'s drag handler and `ticket-detail.ts`'s `startPipeline()` both
show a plain "Pipeline started." toast instead of the old "Pipeline complete - testing
passed/failed…" one; `ticket-question-panel.ts`'s `submitAnswer()`/`retry()` never read the
response body at all, so they needed no change beyond the service's return type. Either page's
own poll (see above) picks up the ticket landing on `ForReview`/`Blocked` once the run actually
finishes.

**`ticket-detail.html`'s "Start"/"Run Pipeline" button is replaced with a plain "Pipeline
running…" label whenever `ticket.pipelineRunning` is true, instead of staying visible and
clickable.** `TicketDto.pipelineRunning` (see [docs/application.md](application.md) for
`IPipelineRunTracker`) is the only reliable signal for this - `ticket.status === 'InProgress'`
alone can't distinguish an actively-running pipeline from one that's idle and genuinely waiting
for a manual trigger, and the component's own local `starting()` signal only covers the few
hundred milliseconds until the detached-run response comes back, not the run itself (which can
take minutes). Since `startPipeline()` already calls `refresh()` right after that response - and
the response itself already reflects `PipelineRunning: true`, `IPipelineRunTracker.MarkRunning`
having run synchronously before the HTTP call even returns - the button disappears as soon as
that immediate refetch resolves, without waiting for the next regular poll tick.

**Drag-and-drop is mapped to the API's actual transition endpoints, not a generic status
setter.** There is no `PUT /tickets/{id}/status`; a ticket only moves between columns through
specific actions (`start`, `move-to-review`, submitting a `Review`). The board
(`features/board/board.ts`) uses Angular CDK drag-and-drop purely as the *gesture* — dropping a
card on a column looks up the `(from, to)` pair and calls the matching endpoint directly, with no
dialog in between (`ToDo → InProgress` calls `POST /tickets/{id}/start`; `InProgress → ForReview`
calls `move-to-review` directly; `ForReview → Done`/`ForReview → InProgress` opens the review form
pre-set to Approve/RequestChanges). Every combination `handleTransition` doesn't implement (e.g.
`ToDo → Done`, `ToDo → ForReview`, `InProgress → Done`) is kept out of the drag gesture entirely
rather than rejected after the fact: `connectedIdsFor(status)` (backed by the module-level
`VALID_DRAG_TARGETS` map) only lists the specific columns `handleTransition` actually handles for
that source status, and `cdkDropListConnectedTo` is one-directional, so e.g. `ForReview` lists
`Done`/`InProgress` as drop targets without `Done`/`InProgress` listing `ForReview` back. A picked-up
card with no valid target just snaps back into place - CDK fires `cdkDropListDropped` on the
*origin* list in that case (`previousContainer === container`), which `onDrop` already no-ops on
before `handleTransition` ever runs. That makes `handleTransition`'s final `notifications.error(...)`
branch unreachable through the board UI; it exists only as a backstop against `VALID_DRAG_TARGETS`
drifting out of sync with `handleTransition` itself. `features/ticket-detail`
renders the same "start"-triggering button (labeled "Start" or "Run Pipeline" depending on ticket
status) for the case where a human wants to (re-)trigger the pipeline without going through the
board — e.g. retrying after a failed run, or after a review's `RequestChanges` sent the ticket
back to In Progress.

**The two direct-transition drops (`ToDo → InProgress`, `InProgress → ForReview`) move the card
locally before the server confirms anything, instead of waiting on the response or the next poll
tick.** `handleTransition`'s private `setTicketStatus` helper writes the target status into the
`tickets` signal immediately on drop; `ticketsByStatus` (the `computed` the two `cdkDropList`s
bind to) picks it up on the very next change-detection cycle, so the card lands in the new column
right away and CDK's own drag animation doesn't get contradicted a moment later by the bound array
snapping back to the pre-drop grouping. This matters most for `start`: the pipeline runs detached
(see [docs/application.md](application.md)), so the `POST /tickets/{id}/start` response comes back
before the background run has assigned an agent and can still report the ticket as `ToDo` -
without the local move, the card would jump to In Progress on drop and then immediately jump back
to To Do when that response landed. The success handler pins the status to the drop target rather
than trusting the response's (possibly stale) `status` field for this reason; `refreshTrigger$`
still fires so `PipelineRunning` and every other field catch up on the next tick. `move-to-review`
doesn't have this lag - `MoveToReviewAsync` updates the status synchronously - but it takes the
same optimistic-then-confirm path for consistency and so a slow request doesn't leave the card
sitting in its old column for the round trip. Either handler's `error` callback calls
`setTicketStatus` again to put the card back where it started; the interceptor-driven toast (see
above) already reports the failure, so neither adds its own.

**Cancelling a ticket is a detail-page button, not a board drop target.** `Cancelled` is a real
`TicketStatus` value but deliberately isn't one of the `BOARD_COLUMNS` — "give up on this
ticket" doesn't fit the drag-a-card-between-columns metaphor the way `start`/`move-to-review`/
`review` do. Instead, `features/ticket-detail` shows a **Cancel Ticket** button (for `ToDo`/
`InProgress`/`ForReview`/`Blocked` tickets) that uses the same `confirm()`/`prompt()` pattern as
deleting an instruction template — a plain browser confirm, then an optional reason — rather than
a dedicated modal. `board.ts`'s `ticketsByStatus` grouping filters `Cancelled` tickets out
entirely, so a cancelled ticket simply disappears from the board's kanban columns. It's not lost,
though: a **Cancelled Tickets** button in the board header
(`features/board/cancelled-tickets-modal`) opens a modal that fetches
`listForSprint(sprintId, 'Cancelled')` (the same `TicketsService` method the board itself uses,
just with the API's existing `status` query filter) on open and lists each cancelled ticket's
title, last-updated time, and cancellation reason (if any), linking through to its detail page.

**`Blocked` *is* a board column, unlike `Cancelled` - the difference is that a blocked ticket
needs a human to notice and act on it, so it stays visible in the normal kanban flow rather than
disappearing.** `BOARD_COLUMNS` (`board.ts`) has 5 entries now (To Do/In Progress/Blocked/For
Review/Done); the column grid switched from a fixed `col-xl-3` (which only divided evenly for 4
columns) to Bootstrap's auto-sizing `col-xl` so any number of columns stays evenly split without
a per-column-count class. Blocked is a drag-and-drop dead end in both directions - entered only
by `OrchestrationService` (a stage's clarifying question, a proceed-or-cancel decision, or a
Git/LLM failure, see [docs/application.md](application.md)) and left only via the ticket detail page's answer/retry
actions, never a manual drag. Concretely, `VALID_DRAG_TARGETS['Blocked']` is `[]` and no other
status lists `Blocked` as a target, so `connectedIdsFor('Blocked')` returns `[]` - nothing can be
dropped into or out of it - simpler than adding a new per-card `cdkDragDisabled` binding, since an
unconnected `cdkDropList` already can't accept a drop and a picked-up card with nowhere valid to
land just no-ops back into place. Clicking through to ticket detail is the only way to see *why* a
ticket is blocked and to do anything about it - see `TicketQuestionPanel` below.

**`TicketQuestionPanel` (`features/ticket-detail/ticket-question-panel`) is the ticket-detail
counterpart to the board's `ChatPanel` - same message-thread shape, different data source and
purpose.** It renders every `TicketQuestionDto` for the ticket as a thread entry (the question,
decision, or failure text, plus the human's answer once one exists, styled the same left/right
message-bubble way `ChatPanel` styles Assistant/User turns) and, only for the ticket's current
`Pending` question, a footer action: an answer textarea for a `Question`- or `Decision`-kind
entry, or a **Retry** button for a `Failure`-kind one (there's nothing to type for a failure - see
`TicketQuestionsService.retry`). Unlike `ChatPanel`'s single-line composer, this answer field is a
`<textarea>` so a plain Enter keypress wouldn't submit it on its own - `(keydown.enter)` calls
`onAnswerKeydown`, which sends on Enter (preventing the default newline) and falls through to
insert a newline as normal on `Shift+Enter`; `submitAnswer` itself now also no-ops while a submit
is already in flight, since Enter isn't covered by the **Send** button's own `[disabled]` binding.
A `Decision`-kind entry's textarea is preceded by a fixed hint
telling the human an agent can't cancel a ticket itself, and to use the ticket's own **Cancel
Ticket** button above instead of answering if that's the right call - answering only resumes the
pipeline, it never triggers a cancellation itself. Unlike `ChatPanel`, it can't rely on "only changes in response to
what this component itself sent," since a pipeline agent can post a new blocking question
asynchronously - so `ticket-detail.ts`'s existing 10s poll was broadened from fetching just the
ticket to `forkJoin({ ticket, questions })`, fetching both every tick instead of adding a second,
separately-timed poll. The panel renders whenever the ticket has at least one question in its
history, not only while currently `Blocked` - like the Reviews card, past questions stay visible
as a record even after the ticket moves on.

**`AgentEventLogPanel` (`features/ticket-detail/agent-event-log-panel`) is a passive, chronological
timeline of every `TicketAgentEventDto` for the ticket - "Research agent started", "Coding agent
completed" (with its result text), and so on - sitting directly below `TicketQuestionPanel` in the
right column.** It follows the same `input<T[]>([])`/no-own-fetching shape as `TicketQuestionPanel`
(`ticket-detail.ts` fetches via `TicketAgentEventsService.listForTicket` and passes the array down),
computing each row's label client-side from `role` + `kind` (`"{Role} agent {verb}"`, or `"Pipeline
{verb}"` when `role` is null - a pre-stage failure) rather than storing a canned string server-side.
`result` (the agent's output, the question/decision text, or the failure message) renders in the
same `white-space: pre-wrap` box `TicketQuestionPanel` uses for `prompt`/`answerText`, shown for
every kind except `Started` (which has none yet). `ticket-detail.ts`'s `forkJoin` grew a third key,
`agentEvents`, alongside `ticket`/`questions`, refetched on the same merged SSE+safety-poll+manual-
refresh stream - reacting to the new `TicketAgentEventLogged` SSE type needs no extra filtering
logic, since the page's SSE subscription already merges every project event by `ticketId` regardless
of `type`. `StatusBadge`'s shared `BADGE_CLASS_BY_VALUE` map (`shared/components/status-badge`)
gained `Started`/`Completed`/`Failed` entries for this panel's kind badges (`Blocked` already existed,
reused from `TicketStatus`). A row also shows a `meta()` line (e.g. "120 in / 340 out tokens ·
5.5s") built client-side from `inputTokens`/`outputTokens`/`durationMs` when present - `null` on
either pair suppresses that half of the line entirely, so a `Started` row (which has neither) shows
nothing and a `Failed` row (duration only, no token counts) shows just the duration.

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
entirely: it takes `projectId` as input (the conversation itself stays project-scoped - see
[docs/application.md](application.md#liveagentchat--the-live-agent-chat)) and loads the project's
list of Live Agent chat sessions itself via `LiveAgentChatService.listConversations` on init (an
`effect()` reacting to the `projectId` signal input). It also takes a `sprintId` input - used only
when approving a drafted ticket (see below), since the sprint whose board this panel happens to be
rendered alongside is where an approved draft lands. A project can have any number of sessions - anyone with project
access can start their own via **+ New chat** (`createConversation`, with a blank title so the
backend falls back to `"New chat"`), and everyone with project access sees the same list and can
select any session from it (a native `<select>` in the header, each option showing the session's
title and `createdByName`) - selecting one calls `listMessages(projectId, conversationId)` to load
just that session's history. `ChatPanel` auto-selects the most recently updated session (position
0 - the list is already sorted that way by the API) whenever the loaded conversation list no
longer contains the currently selected id (covers both first load and the project changing), and
shows an empty-state prompt with the composer disabled when the project has no sessions yet.
Renaming (✏️ button next to the dropdown) swaps the dropdown for an inline `renameForm` text input
scoped to the currently selected session - `saveRename` calls `LiveAgentChatService.renameConversation`
and splices the updated title back into the `conversations` signal; any project member can rename
any session, not just the one they started. Sending a message still appends the user's message
optimistically before the `POST` resolves - stamping the optimistic message's `senderName` from
`AuthService.currentUser()` itself, since the real value only comes back once the `POST` response
arrives - but now targets whichever conversation is currently selected
(`sendMessage(projectId, conversationId, ...)`). Each message bubble renders a small label above
it with the sender's name (`ChatMessageDto.senderName`, falling back to `'You'` if it's ever
missing) for a user message, or `'Live Agent'` for an assistant one. An assistant message
carrying `proposedTicketTitle`/`proposedTicketDescription` renders as an approval card with
**"Create ticket"** and **"Reject"** buttons side by side in the chat thread - clicking either
calls `LiveAgentChatService.approveTicket(projectId, conversationId, messageId, { sprintId, title, description })`/
`rejectTicket(projectId, conversationId, messageId)` - `sprintId` is always the panel's own
`sprintId()` input, sent automatically with no picker UI, since the panel is only ever shown
alongside one sprint's board - which returns the updated `ChatMessageDto`
(now carrying `createdTicketId` or `ticketRejected: true`); `ChatPanel` splices that updated
message back into its `messages` signal in place, which is what swaps the button pair for a
"Ticket created" or "Ticket rejected" badge. Because that decision is persisted on the message
server-side rather than tracked in a local-only signal, the badge (not the buttons) is also what
renders after a reload, or for a second user viewing the same shared conversation - nobody can
approve *or* reject the same draft twice, and a single `processingTicketMessageIds` signal
disables both buttons while either request is in flight so a double-click can't fire both actions
at once. **The draft is editable before approval**: an undecided approval card also shows a
pencil (✏️) button next to the title; clicking it calls `startEditTicket`, which seeds a shared
`ticketEditForm` (title/description) from that message's current proposal and swaps the static
title/description for an inline form with its own **"Create ticket"**/**"Reject"**/**"Cancel"**
row (mirroring the single-editor-at-a-time pattern `renameForm` already uses for session titles -
`ticketEditingMessageId` tracks which one message, if any, is being edited). Both the title input
and the description textarea are editable; the description textarea also grows to fit its
starting content instead of staying at its fixed `rows="3"` and scrolling internally - a
`viewChild('ticketDescriptionTextarea')` signal paired with an `effect()` keyed off
`ticketEditingMessageId` resizes it (`scrollHeight`-based) the moment the edit form mounts, and an
`(input)` handler keeps re-measuring it as the user types. Submitting from that
form still calls `approveTicket(message)`, which checks `isEditingTicket(message)` to source the
title/description from `ticketEditForm`'s current values instead of the message's own
`proposedTicketTitle`/`proposedTicketDescription` - so the ticket is created with whatever the
user last typed, not the model's original draft. `cancelEditTicket` discards the in-progress edit
and reverts to the static card; approving or rejecting an edited draft also clears the editing
state on success. The newly created ticket simply shows up on the board once the existing 8s
ticket poll (above) picks it up - no new refresh plumbing was added for this.

**The chat panel collapses to a thin strip so the board can reclaim its width, with the resize
itself animated.** The collapsed flag lives in `Board` (`chatCollapsed`, a plain signal), not in
`ChatPanel` itself, since it drives the sibling board column's width too - `board.html` puts a
`board-chat-col`/`board-columns-col` class pair on the two columns instead of Bootstrap's
`col-12 col-lg-4`/`col-lg-8` utility classes, because swapping discrete Bootstrap col classes on
collapse can't be transitioned (no shared animatable property across a breakpoint's class swap).
`styles.scss` gives `.board-chat-col` a fixed width (`22rem` expanded, `4rem` collapsed via
`.board-chat-col--collapsed`) with a CSS `transition`, and `.board-columns-col` just
`flex: 1 1 auto` to fill whatever's left - so the board's own width animates for free as the
chat column's width tweens, with no separate transition needed on it. Below the `lg` breakpoint
both stay full-width and stacked, same as before. `ChatPanel` takes `collapsed` as an input and
emits `collapsedChange` on its own header/strip toggle button, the same input/output shape as
every other parent-owned-state component in this codebase (e.g. `CreateTicketForm`'s
`open`/`closed`) - it doesn't own the flag, just renders according to it and asks the parent to
flip it. Collapsed, `ChatPanel` renders a single round icon button (💬 plus a chevron) in place
of its usual header/body/footer; since this project has no `@angular/animations` dependency
(see `package.json`), the "show/hide" animation is a plain CSS `@keyframes` fade+slide
(`chat-panel-fade-in`) on the card, which plays automatically whenever the card enters the DOM -
i.e. on every collapse/expand, since `@if`/`@else` swaps the whole card rather than hiding a
persistent one.

**The chat panel stretches to fill the viewport below the navbar/page header, not a fixed
`65vh`.** `.chat-panel-card` (styles.scss) sets `height: calc(100vh - 11rem)` - `11rem`
approximates the sticky navbar plus the page's own top padding and header block above the board
row - instead of the old `max-height: 65vh` on just the message list, which left dead space below
a short conversation and cut a long one off early regardless of viewport size. `.chat-panel-body`
keeps `min-height: 0` so the message list (`flex-grow-1 overflow-auto`) actually scrolls within
that fixed card height instead of growing past it, a common flexbox-scroll-container gotcha.

**The board's sprint name, column colors/icons, and column set are two different concerns kept
separate on purpose.** The header shows `sprint().name` (the board's actual title now — a sprint
is what owns the ticket board) with a back-link to `project().name`'s sprint list; both are loaded
the same way `ticket-detail` loads its project — separate subscriptions alongside the tickets poll,
one keyed off the route's `projectId`, one off `sprintId` — so a board reached from a bookmark or a
shared link (`/projects/:projectId/sprints/:sprintId/board`) is unambiguous about which project
*and* sprint it belongs to. `BOARD_COLUMNS` in
`board.ts` carries a per-status `icon` and `accentClass` (⏳/🔧/🚫/👀/✅, one accent color each)
purely for visual scannability of the 5 pipeline stages/states. `status-badge.ts`'s ticket-status
badge colors (`ToDo`/`InProgress`/`Blocked`/`ForReview`/`Done`, used in ticket detail, reviews,
etc.) intentionally reuse this same accent palette — `text-bg-primary`/`text-bg-warning`/
`text-bg-danger`/`text-bg-success` are the same colors as `$board-todo-color`/
`$board-inprogress-color`/`$board-blocked-color`/`$board-done-color`, all aliased to Bootstrap's
own `$primary`/`$warning`/`$danger`/`$success` (so `Blocked` needed no new custom badge class,
unlike `ForReview`'s purple, which has no built-in Bootstrap variant and gets its own
`.text-bg-forreview` class in `styles.scss` set to `$board-forreview-color`). So a ticket's badge
always matches the column it sits in, wherever that badge is shown. The custom CSS for both
(`.board-column--*`, `.board-column-title`, `.text-bg-forreview`, `.ticket-card` hover) lives in
the single global `src/styles.scss`, not per-component `styleUrls` - this project has never used
scoped component styles (everything else is Bootstrap utility classes in the template), so a new
per-component stylesheet would be a second, competing styling convention rather than a small
addition to the existing one.

**`btn-danger`/`btn-outline-danger` text color is pinned explicitly, not left to Bootstrap's
contrast guess.** The custom `$danger` (`#f04923`) has only a ~3.7:1 contrast ratio against white,
under Bootstrap's `$min-contrast-ratio` (4.5) - so `button-variant`'s built-in `color-contrast()`
picks black text for a filled `.btn-danger` instead of white. `styles.scss` overrides `.btn-danger`
(all states) to `color: #fff` and `.btn-outline-danger`'s plain/unhovered state to `color: $danger`
(its hover/active/checked fill states go white too, matching the filled button) rather than
raising `$min-contrast-ratio` globally, which would also affect `$warning`/`$board-forreview-color`
button and badge text this codebase already relies on.

**The sprint list's per-status count badges reuse the board's icon/color mapping, computed
server-side, not fetched per sprint - the project list carries no ticket counts at all now.**
`SprintDto.ticketStatusCounts` (`TicketStatusCountsDto` - one int per board-relevant status,
`Cancelled` excluded like `BOARD_COLUMNS`) is populated by `SprintService` from a single grouped
`ITicketRepository.GetStatusCountsBySprintAsync` query across every listed sprint, so
`features/sprints/sprint-list` renders its badge row straight off the existing
`SprintsService.list(projectId)` response - no extra per-card request, consistent with this
codebase's "avoid overfetching" convention (see `TicketRepository` in
[docs/infrastructure.md](infrastructure.md) and `SprintService` in
[docs/application.md](application.md)). `sprint-list.ts`'s `STATUS_SUMMARIES` constant mirrors
`BOARD_COLUMNS`' icon per status and `status-badge.ts`'s badge-color mapping, kept as its own
small array rather than reusing either component directly, since it renders a count badge, not a
ticket's own status label. `features/projects/project-list` dropped this badge row entirely along
with the sprint-date fields it used to show, since a project's tickets are now spread across
however many sprints it has - its card is just name/description/remote URL/clone status.

**A `Cloning` project's card shows a live progress bar, driven by its own SSE subscription - not
a poll.** `project-list.ts` opens `projectEventsService.stream(project.id)` for every project whose
`status` is `Cloning` (both right after the initial `reload()` and right after creating a new
one, since a freshly-created project's list entry already comes back `Cloning`), filtered to
`ProjectCloneProgress`, and keeps that subscription open only for as long as the project stays
`Cloning` - the final event (`status: 'Ready' | 'Failed'`) both unsubscribes and patches that one
project's `status`/`cloneFailureReason` in place in the `projects` signal, rather than triggering a
full `reload()` just to pick up one project's outcome. Progress numbers themselves
(`receivedObjects`/`totalObjects`/`receivedBytes`) live in a separate `cloneProgress` signal keyed
by project id, not folded into `ProjectDto`, since `ProjectDto` has no byte/object-count fields and
never needs them once a clone finishes. The bar is indeterminate (Bootstrap's
`progress-bar-striped`/`progress-bar-animated`) until Git reports a nonzero `totalObjects` - a
repo's total object count isn't known until enough of the clone negotiation has happened - and a
`Failed` card shows `cloneFailureReason` in an inline alert instead. "View Sprints" is disabled
(`[class.disabled]`, `routerLink` set to `null`) for any project that isn't `Ready`, since a
ticket-creation attempt under a `Cloning`/`Failed` project's sprint would just fail with
`ProjectNotReadyException` (see [docs/application.md](application.md)).

**Remove is disabled up front on the sprint list, not just rejected after the click - the project
list no longer has this pre-check.** Each Admin-only card in `sprint-list.html` has a **Remove**
button alongside **Edit**, gated by `canRemove(sprint)` - `true` only when
`sprint.ticketStatusCounts.inProgress === 0 && ...forReview === 0`, reusing the same counts already
on `SprintDto` rather than a separate request. This mirrors, rather than replaces, the server-side
check in `SprintService.RemoveAsync` ([docs/application.md](application.md)): the button being
enabled is just a UX shortcut, since the counts backing it can go stale between renders (another
user starting a ticket, an SSE-driven board update elsewhere) - the 409 the interceptor would
surface from a stale click is still the real guard. `remove()` confirms via the browser's native
`confirm()` (same pattern as `instruction-templates.ts`'s `delete()`), tracks in-flight removals in
a `removingIds` signal so the clicked card's button reads "Removing..." and stays disabled, and on
success just filters the sprint out of the local `sprints` signal instead of a full `reload()`.
`project-list.ts`'s own `remove()` follows the same confirm/track/filter shape, but since
`ProjectDto` no longer carries ticket counts, its **Remove** button has no client-side pre-check at
all - a project with an active ticket in any sprint just surfaces the 409 as a toast, same as a
stale sprint-list click would.

**`features/backlog/backlog.ts`** (route `projects/:projectId/backlog`, reached via a **Backlog**
button in `sprint-list.html`'s header, alongside **Agents**) **lists a project's tickets that have
no sprint yet** (`TicketDto.sprintId: string | null`, `TicketsService.listBacklog(projectId)`).
Unlike the board, it's a flat `list-group`, not columns - a backlog ticket can only ever be `ToDo`
or `Cancelled` (nothing can move it to `InProgress` without a sprint - see
[docs/application.md](application.md)), so there's no drag-and-drop, and no separate "Cancelled"
modal the way the board has one. **"New Ticket"** reuses the board's own `CreateTicketForm`
component wired to `TicketsService.createBacklog` instead of the board's `create`. Each row has an
inline `<select>` of the project's sprints (`SprintsService.list(projectId)`) plus an **Assign**
button calling `TicketsService.assignToSprint(ticketId, { sprintId })`; on success the ticket is
filtered out of the local `tickets` signal (same optimistic-remove pattern as `sprint-list.ts`'s
own `remove()`) rather than a full `reload()`, since it's no longer part of this list once
assigned. The `<select>`'s value is read via a template reference variable
(`#sprintSelect`/`sprintSelect.value`) rather than a `FormGroup` - the same "ad-hoc filter input"
exception the git-diff branch pickers already use (see "Code style notes" below), since it's one
plain value read on a button click, not a form with its own validation. Deliberately **not**
reusing `TicketCard` for these rows: `TicketCard`'s whole card is a Bootstrap `stretched-link`
(see "Branch names are links..." below), which would sit on top of - and swallow clicks meant for
- the inline sprint `<select>`/**Assign** button; a plain row with an ordinary `<a>` avoids that
z-index fight entirely.

`features/ticket-detail/ticket-detail.ts` gained a `backLink()`/`backLabel()` computed pair: a
ticket with a `sprintId` still links back to its sprint's board (as before), but a backlog ticket
(`sprintId: null`) has no board to return to, so it links back to `/projects/:projectId/backlog`
instead, labeled "Back to backlog" rather than "Back to board".

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
parses a single unified-diff blob (`GitFileDiff.Patch`, `Conflict.ConflictingDiffContent`,
`Conflict.ResolvedContent`) line-by-line into add/remove/hunk/header/context spans. This was
small enough to not justify an extra npm dependency.

**Per-file, collapsible commit diffs with a before/after view.** A commit's `DiffContent` is
one `git diff`-style blob that can span multiple files (LibGit2Sharp's `Patch.Content`
concatenates one `diff --git a/... b/...` block per changed file). `core/utils/diff-parser.util.ts`
splits that blob into a `FileDiff` per file (path, add/remove counts, new/deleted/renamed flags,
parsed hunks) and pairs each hunk's removed/added lines into before/after rows for a split view.
`shared/components/commit-diff-viewer` renders one collapsed-by-default section per file - click
a file's header to expand it, with a per-file "Unified" / "Before/After" toggle - so a multi-file
commit isn't one unbroken wall of diff text. Used on the ticket detail page's Commits list;
`diff-viewer` remains the renderer for the single-file diff blobs above.

**Instruction templates are a global admin page, not project-scoped.** `features/admin/
instruction-templates/` follows the same shape as `features/admin/users/` (a list + a
create/edit modal, route gated by `adminGuard`, nav link only shown when
`authService.isAdmin()`) but isn't nested under a project route, since `InstructionTemplate`
belongs to no project. The one place it's consumed outside its own admin page is
`features/agents/instruction-editor/instruction-editor.ts`, which now takes an `agentRole` input
(the agents list already had this — it just wasn't being looked up and passed down before) and
fetches templates filtered to that role, one **Template** dropdown per instruction type. For
Guideline and Requirement, the dropdown is always rendered and always enabled (never disabled,
even with zero matching templates) - the placeholder option's label switches between "Populate
from template..." and "No templates for this role/type yet" so an empty dropdown doesn't read as
broken. Selecting a template fills the matching textarea (`form.get(type).setValue(...)`) and the
`<select>` keeps showing the picked option, tracked in a `selectedTemplateIds` signal keyed by
instruction type — it's still just a content fill, not a persisted association (the selection
resets on save/reload); the existing "Save & Ingest" flow is what actually commits anything, same
as typing the content by hand. The Constitution row is the one exception: for any agent whose
`agentRole` isn't `'Custom'`, the component disables that `FormControl` (`Agent.AddInstructionVersion`
rejects the edit server-side regardless, per [docs/domain.md](domain.md#default-agent-instructions))
and the template renders a "Fixed" badge and explanatory text instead of the dropdown, so there's
nothing to pick from or submit for that field.

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
  `CommitDiffViewer`, `StatusBadge`).
- Every HTTP service is a thin, one-method-per-endpoint wrapper (no generic `ApiClient<T>`) —
  mirrors the API's own per-aggregate-repository philosophy
  (see [docs/application.md](application.md)).
- Forms are typed Reactive Forms (`FormBuilder.nonNullable.group({...})`) everywhere a form
  exists — no template-driven forms except a couple of ad-hoc filter inputs (`FormsModule`
  `[(ngModel)]`) where a full `FormGroup` would be overkill (e.g. the git-diff branch pickers).
- Reusable dialogs (`create-ticket-form`, `review-form`, `project-form`, `sprint-form`,
  `user-edit-modal`) all wrap the shared `Modal` component and follow the same `open` input /
  `closed` output / `<action>` output contract, including the "Add Custom Agent" dialog on
  `features/agents/agents.ts` (name only - the agent starts with no instructions, per
  `WorkflowController`, see [docs/api.md](api.md)).
- `project-form`'s Access Token field (`type="password"`) is the first masked input in this
  codebase - no prior precedent existed to follow. It's required when creating a project and
  optional when editing (blank = keep the currently stored token); the validator is
  added/cleared on the `accessToken` control inside the same `effect()` that already resets the
  form per the `project()` input. Remote URL is rendered as read-only text instead of a form
  control when editing, since `Project.RemoteUrl` is immutable after creation. It no longer has a
  base branch or sprint fields at all - those moved to `sprint-form`, built directly off
  `project-form`'s old shape (same reactive-form/modal pattern) once `Sprint` took over owning
  them: `baseBranch` (required, defaults to `'main'`) plus the same three optional sprint fields
  (`sprintStartDate`/`sprintEndDate` as native `type="date"` inputs, `sprintGoal` as a textarea) -
  all unvalidated client-side beyond the server's end-before-start check, since they're purely
  informational. `create-ticket-form` has a required `acceptanceCriteria` textarea
  alongside title/description, following the same `Validators.required` + inline error-message
  pattern as `title`. The Live Agent chat's ticket-approval edit form (`chat-panel`'s
  `ticketEditForm`) has the same required `acceptanceCriteria` control, since an approved draft
  also creates a real `Ticket`.
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
- **Live Agent chat has no real-time updates yet.** `features/board/chat-panel` still only
  refreshes on the user's own actions (sending a message, switching sessions) — two people in the
  same conversation don't see each other's messages without a manual reload/reselect. Extending
  `ProjectEventsService`'s stream (or the `ProjectEvent` type set) to cover new chat messages would
  be the natural next step, deliberately left out of the board/ticket-detail SSE conversion above
  to keep that change scoped to the polling it was replacing.
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
