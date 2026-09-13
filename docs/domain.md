# Domain Module (`TeamPilot.Domain`)

[← Back to README](../README.md)

## Purpose

`TeamPilot.Domain` holds the business rules and entities that everything else in the system
operates on. It has **zero NuGet package references** — no EF Core, no ASP.NET Core, nothing —
so the rules encoded here (what states a ticket can transition through, who can approve what,
how a refresh token gets revoked) can be tested and reasoned about without any framework or
database in the picture.

Its role in the architecture is the innermost ring of Clean Architecture: every other project
depends on it, it depends on nothing.

## Architectural decisions

**Rich domain models, not anemic data containers.** Every entity has private setters and
exposes behavior methods instead of public property setters. `Ticket.Approve()` guards that
the ticket is `ForReview` before transitioning to `Done` and throws
`InvalidTicketStateTransitionException` otherwise; there is no way to set `Ticket.Status`
directly from outside the class. This was a deliberate trade-off: it costs more boilerplate
(factory methods, guard clauses) than a plain POCO, but it makes illegal states
unrepresentable through the public API, which matters more here given how many services
(Orchestration, ApprovalGate, Conflicts) mutate the same aggregates.

**A shared `Entity` base class** ([`Common/Entity.cs`](../src/TeamPilot.Domain/Common/Entity.cs))
provides `Id`, `CreatedAtUtc`, and `UpdatedAtUtc` with a protected `MarkUpdated()` helper.
`Id` is assigned client-side (`Guid.NewGuid()`) at construction time, not by the database —
see [docs/infrastructure.md](infrastructure.md#value-generation) for why this specific choice
required an explicit EF Core configuration fix.

**Aggregate boundaries are intentionally loose for child records.** `Commit`, `Review`, and
`Conflict` reference their `Ticket` by `TicketId` only (no navigation back to `Ticket`), and
`Ticket` doesn't strictly enforce "only mutate my children through me" for `Conflict` (the
`ConflictResolutionService` mutates a `Conflict` directly via its own ID, since the API
addresses conflicts by their own ID, not nested under a ticket route). This is a pragmatic
deviation from strict DDD aggregate rules, made explicitly to match how the API needs to
address these records — not an oversight.

**`StageExecution` deliberately isn't added via `Ticket` at all, unlike `Commit`/`Review`.**
`Commit.AddCommit`/`Ticket.RecordReview` add to collections `Ticket` eagerly loads on every
`GetByIdAsync` (called at the start of essentially every ticket-related use case), which is fine
for records created a handful of times per ticket. `StageExecution` is created on *every* stage
invocation - every stage, every pipeline run, every loop-back retry - so a long-lived ticket could
accumulate far more of them than commits or reviews ever would. It's created directly via
`StageExecution.Create` and queried standalone through its own repository (the same pattern
`WorkflowStage` already uses), so nothing pays the cost of loading that growing history just to
load a `Ticket`. **`TicketQuestion` follows this exact same pattern** - a ticket can be blocked
and resumed any number of times over its life (a clarifying question, or a Git/LLM operational
failure - see [docs/application.md](application.md)), so it's queried standalone through
`ITicketQuestionRepository` rather than being another eagerly-loaded `Ticket` collection.

**`Ticket.Block()`/`Unblock()` model a pipeline pausing mid-run, not a terminal state.** Unlike
`Cancel`, `Block` only transitions from `InProgress` (the pipeline has to actually be running for
there to be anything to pause) and `Unblock` only from `Blocked` back to `InProgress` - there's no
`Blocked -> ForReview` shortcut, so a paused ticket always goes through the pipeline again
(`OrchestrationService.RunPipelineAsync`) to reach review, same as any other run.
`OrchestrationService` calls `Block()` itself, either when a stage's response contains a
`QUESTION: ...` marker or when a known operational failure occurs
(`GitOperationException`/`LlmOperationException`) - either way a `TicketQuestion` is created
recording what happened, which is what a human answers or retries against
(`TicketQuestionService`) to call `Unblock()` and resume. `ApprovalGateService` also calls
`Block()` from its own backstop around the detached `RequestChanges` pipeline re-run (see
[docs/application.md](application.md)), for the same reason and with the same `Failure`-kind
`TicketQuestion` - any exception type `OrchestrationService` doesn't already handle itself.

**Domain exceptions signal invariant violations, not application errors.** Every domain
exception derives from `DomainException` ([`Exceptions/DomainException.cs`](../src/TeamPilot.Domain/Exceptions/DomainException.cs)),
which the API layer maps to HTTP 409 Conflict. `NotFoundException`, `ValidationException`, and
`ForbiddenException` are deliberately *not* domain exceptions — they live in the Application
layer, because "the requested id doesn't exist" or "you don't have permission" are use-case
concerns, not violations of a business rule about the entity's own state.

**`Ticket.UnlinkBranch` only works once `Cancelled`, and it's the only thing that frees a branch
name for reuse.** The `(ProjectId, BranchName)` uniqueness enforced in Infrastructure (see
[docs/infrastructure.md](infrastructure.md)) is otherwise permanent - a branch stays claimed by
whichever ticket linked it first, even after that ticket is `Done` or `Cancelled`, because the
branch may still carry commits nobody's accounted for. `UnlinkBranch` is the explicit exception:
it's only called after the branch has actually been deleted from Git — automatically as part of
`TicketService.CancelAsync`/`ApprovalGateService.RejectAsync`, or manually via
`TicketService.DeleteBranchAsync` as a fallback — at which point there's nothing left to protect
against, so clearing `BranchName` is safe. It
throws `InvalidTicketStateTransitionException` from any other status - deleting an active
ticket's branch out from under it would leave `Ticket.BranchName` pointing at nothing.

**`Ticket.Cancel` is a terminal abandon, reachable from any pre-merge status.** Unlike `Approve`
(only from `ForReview`), `Cancel` accepts `ToDo`, `InProgress`, `ForReview`, or `Blocked` — a
ticket can be abandoned at any point before it's merged, including while paused waiting on a
clarifying question or a failure retry, since nothing about abandoning it depends on how far the
pipeline got. It's not allowed from `Done`: the work is already merged, so there's nothing
left to cancel, and once `Cancelled` there's no path back (no "reopen") in this pass. The
optional reason is trimmed and stored as `CancellationReason` rather than discarded, since
that's the whole point of the feature — capturing *why* (e.g., a requirement changed) is more
useful later than a bare status flip. `ReviewDecision.Reject` reuses this same method (passing
the review's comments as the reason) rather than introducing a separate status - see
[docs/application.md](application.md) for how `ApprovalGateService` combines it with an
automatic branch delete.

**Dependencies:** none (this is the point).

## Entities at a glance

| Entity | Represents | Key behavior methods |
|---|---|---|
| `Project` | A project tied to a remote Git repo, with its own agents and ticket board | `Create`, `AssignSandboxPath`, `UpdateDetails`, `RotateAccessToken` |
| `Ticket` | A unit of work on the Kanban board | `AssignAgent`, `LinkBranch`, `UnlinkBranch`, `AddCommit`, `MoveToReview`, `Approve`, `RequestChanges`, `RecordReview`, `RaiseConflict`, `Block`, `Unblock`, `Cancel` |
| `Agent` | An AI agent (Research/Design/Coding/Testing, the standing `LiveAgent`, or an admin-created `Custom` agent) scoped to a project | `Activate`, `Deactivate`, `UpdateConfiguration`, `AddInstructionVersion` |
| `Instruction` | An append-only, versioned constitution/guideline/requirement for an agent | *(created only via `Agent.AddInstructionVersion`)* |
| `WorkflowStage` | One position in a project's admin-configurable agent workflow - references its `Project` and `Agent` by id only | `Create`, `MoveTo`, `SetLoopBack`, `ClearLoopBack` |
| `StageExecution` | An immutable record of one agent's output for one ticket, one per stage invocation - references its `Ticket` and `Agent` by id only | *(created only via `StageExecution.Create`)* |
| `TicketQuestion` | A record of why a ticket was blocked - a clarifying question or a Git/LLM failure - references its `Ticket` and (nullable) `Agent` by id only | `CreateQuestion`, `CreateFailure`, `Answer`, `MarkConsumed` |
| `Conversation` | A project's single, ongoing chat thread with its `LiveAgent` | `Create`, `AddMessage` |
| `ChatMessage` | One turn (user or assistant) in a `Conversation`, optionally carrying a drafted ticket pending approval | *(created only via `Conversation.AddMessage`)*, `MarkTicketCreated`, `RejectTicket` |
| `InstructionTemplate` | A reusable, admin-managed instruction an admin can pick from when editing a real agent's instructions | `Create`, `Update` |
| `Commit` | A fact record of a Git commit produced for a ticket | *(immutable once created)* |
| `Review` | A human approval-gate decision | *(immutable once created)* |
| `Conflict` | A detected merge conflict, with an optional AI-suggested resolution | `RecordAiSuggestion`, `ResolveManually`, `AcceptAiSuggestion` |
| `TicketAgentAssignment` | Join record: which agent is assigned to which ticket | *(created only via `Ticket.AssignAgent`)* |
| `PipelineRun` | A CI/CD status-tracking record for a project | `Start`, `Complete` |
| `User` | An authenticated principal | `UpdateName`, `SetRoles`, `Disable`, `Enable` |
| `RefreshToken` | A rotatable session refresh token | `Revoke` |
| `UserProjectAssignment` | Join record: which projects a user can access | *(managed by repository "set" semantics, not through `User`)* |
| `AuditLogEntry` | An immutable security-event record | *(created once, never mutated)* |

## Default agent instructions

`Agent.Create` seeds every new agent with a current (version 1) `Instruction` for each of the
three `InstructionType`s (Constitution, Guideline, Requirement), via the internal
`AgentDefaultInstructions` lookup keyed by `AgentRole`
([`Entities/AgentDefaultInstructions.cs`](../src/TeamPilot.Domain/Entities/AgentDefaultInstructions.cs)).
The seeded text is generic and technology-agnostic — it describes each role's general
responsibilities (e.g. Research investigates, Coding implements, Testing verifies) without
naming any framework or language, so it applies to any project. `LiveAgent`'s default
instructions instead frame it as "someone who knows this project" the user can ask questions
to, and explicitly constrain it to read-only repository access and to only *drafting* a ticket
(never creating one outright, and only when the user explicitly asks) — see
[LiveAgentChat / the Live Agent chat](application.md#liveagentchat--the-live-agent-chat) for how
those constraints are enforced. An admin edits any agent's instructions the same way: calling
`Agent.AddInstructionVersion` for a type that already has a default adds a new version that
supersedes it, rather than replacing it in place — the default is never mutated, only
superseded. The one exception is `InstructionType.Constitution` on a default/standing role
(anything but `AgentRole.Custom`): once its version-1 Constitution is seeded by `Create`,
`AddInstructionVersion` throws `Exceptions.ConstitutionEditNotAllowedException` for any further
attempt to add a Constitution version on that agent, so its role-defining identity can never
drift out from under the orchestration pipeline's assumptions about what each stage is. Guideline
and Requirement have no such restriction on any role, and a `Custom` agent's Constitution has no
restriction either, since it starts with no seeded identity of its own to protect. The 5
default/standing agents (Research/Design/Coding/Testing/LiveAgent) are still never created or
deleted through the API; the one exception is an admin-created `AgentRole.Custom`
agent (`WorkflowService.CreateCustomAgentAsync`, see [docs/application.md](application.md)),
which starts with **no** seeded instructions at all (`AgentDefaultInstructions.For(AgentRole.Custom)`
returns an empty list) — an admin must add all three types before
`Agent.HasCompleteInstructions` is true, which is required before it can be placed into a
project's workflow. `Agent` instances are still almost never *deleted* through the API — removing
a custom agent from a workflow only removes its `WorkflowStage`, never the `Agent` row or its
instruction history. The one real exception is `WorkflowService.DeleteCustomAgentAsync`, and it's
deliberately narrow: only an `AgentRole.Custom` agent, that isn't currently scheduled into any
workflow, and has never been assigned to a ticket (so there's no history anywhere to lose), can
actually be deleted. Every other agent - the 4 default roles, `LiveAgent`, or a custom agent
that's ever actually run - stays permanent.

## Conversation and ChatMessage

`Conversation.Create(projectId, agentId)` creates a project's single chat thread with its
`LiveAgent`, created lazily on the project's first chat message
(`Conversation` has a unique index on `ProjectId` — see
[`Entities/Conversation.cs`](../src/TeamPilot.Domain/Entities/Conversation.cs)). Each turn is a
`ChatMessage` appended via `Conversation.AddMessage(role, content, proposedTicketTitle?,
proposedTicketDescription?, senderName?)` — an assistant message that drafted a ticket carries
those two optional ticket fields so the "create ticket" approval card in the UI re-renders
correctly after a reload. A user message additionally carries `SenderName`, the display name of
the user who sent it captured at send time (so the chat history keeps showing who said what even
if the user is later renamed or removed) — it's always null for an assistant message. Nothing
about creating a `ChatMessage` creates a real `Ticket`; that only happens if/when the user
approves the draft (see [docs/application.md](application.md)). Approval calls
`ChatMessage.MarkTicketCreated(ticketId)`, which sets `CreatedTicketId`; dismissing a draft
instead calls `ChatMessage.RejectTicket()`, which sets `TicketRejected`. Both throw
`ChatMessageTicketApprovalException` (a `DomainException`, mapped to 409) if the message has no
`ProposedTicketTitle`, if the *other* decision was already made (you can't reject an approved
draft or approve a rejected one), or if that same decision was already made — the latter guards
the entity itself, though the application-layer caller checks `CreatedTicketId`/`TicketRejected`
first and treats a repeat of the same decision as an idempotent no-op rather than ever reaching
that throw in normal use. `CreatedTicketId` being non-null or `TicketRejected` being true is what
the UI uses to keep the "Create ticket"/"Reject" pair from reappearing, including after a reload
or for a different user viewing the same shared conversation.

## Code style notes

- Every entity has a `private` parameterless constructor (for EF Core's materialization) and a
  `public static Create(...)` factory that validates required fields and returns a fully-formed
  instance — never a constructor callers invoke directly.
- Guard clauses throw `ArgumentException` for "this input is structurally invalid" (empty
  title, `Guid.Empty` id) and a specific `DomainException` subtype for "this operation isn't
  legal right now given the entity's current state" (wrong ticket status, conflict already
  resolved).
- Collections are exposed as `IReadOnlyCollection<T>` backed by a `private readonly List<T>`
  field; mutation only happens through named methods, never by exposing the list itself.
- `User.Roles` is the one exception worth calling out: it's a small, fixed-size enum
  collection, so it's mapped by Infrastructure as an EF Core "primitive collection" (a JSON
  array column) rather than a child table — see [docs/infrastructure.md](infrastructure.md).

## Workflow integration

Domain entities are loaded and persisted by Infrastructure repositories (one repository
interface per aggregate, declared in Application) and mutated by Application services calling
their behavior methods. The Domain layer itself never talks to a database, an HTTP client, or
any other entity's repository — it only knows about its own fields and, at most, other
entities' IDs.

## Authentication hooks

The auth-related entities *are* the domain model for authentication and access control:

- `User.Roles` (a `List<UserRole>`: `Admin`, `Analyst`, `Developer`) is the source of truth for
  role-based checks performed in Application services.
- `RefreshToken` models the full rotation lifecycle: `IsActive` = not revoked and not expired;
  `Revoke(replacedByTokenId)` records both the revocation and, when rotating, which token
  replaced it — this chain is what lets `AuthService` detect reuse of an already-rotated token.
- `User.Status` (`Active`/`Disabled`) is checked on every login and token refresh.

The Domain layer does not know about JWTs, cookies, or HTTP at all — token issuance and
validation are Infrastructure concerns (see [docs/infrastructure.md](infrastructure.md#authentication)).

## Example

```csharp
var ticket = Ticket.Create(projectId, "Add health check endpoint", "Expose GET /health");
ticket.AssignAgent(codingAgent);              // ToDo -> InProgress
ticket.LinkBranch("feature/health-check");
ticket.AddCommit(commit);                     // recorded by OrchestrationService
ticket.MoveToReview();                        // InProgress -> ForReview
ticket.RecordReview(Review.Create(ticket.Id, "Alice", ReviewDecision.Approve, "LGTM"));
ticket.Approve();                             // ForReview -> Done
```

## Error handling

Invariant violations throw a `DomainException` subtype (e.g.
`InvalidTicketStateTransitionException`, `InvalidConflictStateTransitionException`,
`InvalidTicketQuestionStateException`). These are allowed to propagate all the way to the
API's `GlobalExceptionHandler`, which maps any `DomainException` to HTTP 409 — the Domain layer
does not catch or wrap its own exceptions.

## Future considerations

- **Domain events:** none are raised today (e.g., "TicketApproved"); if a future feature needs
  to react to state changes (notifications, webhooks) without coupling services directly
  together, a lightweight domain event dispatcher would be the natural next step.
- **Value objects:** `Ticket.Title`, `User.Email`, and `Project.RemoteUrl` are plain `string`s
  today. If validation rules for these grow more complex, promoting them to value objects (e.g.
  an `EmailAddress` type) would centralize that logic.
- **Instruction versioning as its own aggregate:** `Instruction` is currently a child of
  `Agent`. If instruction history needs its own access patterns (e.g. bulk diffing across
  versions, independent pagination) it may outgrow being a pure `Agent` child.
- **Aggregate boundary tightening:** the `ConflictResolutionService` mutating `Conflict`
  directly (bypassing `Ticket`) is a known, intentional simplification (see "Architectural
  decisions" above) — worth revisiting if conflict-related invariants ever need to span
  multiple conflicts on the same ticket.
