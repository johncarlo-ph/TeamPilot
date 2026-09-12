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
it's only called after the branch has actually been deleted from Git (`TicketService.DeleteBranchAsync`),
at which point there's nothing left to protect against, so clearing `BranchName` is safe. It
throws `InvalidTicketStateTransitionException` from any other status - deleting an active
ticket's branch out from under it would leave `Ticket.BranchName` pointing at nothing.

**`Ticket.Cancel` is a terminal abandon, reachable from any pre-merge status.** Unlike `Approve`
(only from `ForReview`), `Cancel` accepts `ToDo`, `InProgress`, or `ForReview` — a ticket can be
abandoned at any point before it's merged, since nothing about abandoning it depends on how far
the pipeline got. It's not allowed from `Done`: the work is already merged, so there's nothing
left to cancel, and once `Cancelled` there's no path back (no "reopen") in this pass. The
optional reason is trimmed and stored as `CancellationReason` rather than discarded, since
that's the whole point of the feature — capturing *why* (e.g., a requirement changed) is more
useful later than a bare status flip.

**Dependencies:** none (this is the point).

## Entities at a glance

| Entity | Represents | Key behavior methods |
|---|---|---|
| `Project` | A project tied to a remote Git repo, with its own agents and ticket board | `Create`, `AssignSandboxPath`, `UpdateDetails`, `RotateAccessToken` |
| `Ticket` | A unit of work on the Kanban board | `AssignAgent`, `LinkBranch`, `UnlinkBranch`, `AddCommit`, `MoveToReview`, `Approve`, `RequestChanges`, `RecordReview`, `RaiseConflict`, `Cancel` |
| `Agent` | An AI agent (Research/Design/Coding/Testing) scoped to a project | `Activate`, `Deactivate`, `UpdateConfiguration`, `AddInstructionVersion` |
| `Instruction` | An append-only, versioned constitution/guideline/requirement for an agent | *(created only via `Agent.AddInstructionVersion`)* |
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
naming any framework or language, so it applies to any project. An admin edits it like any other
instruction: calling `Agent.AddInstructionVersion` for a type that already has a default adds a
new version that supersedes it, rather than replacing it in place — the default is never mutated,
only superseded. This is the only way to customize an agent's behavior — `Agent` instances
themselves are never created or deleted through the API; see "Every project always has exactly
one agent per pipeline role" in [docs/application.md](application.md).

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
`InvalidTicketStateTransitionException`, `InvalidConflictStateTransitionException`). These are
allowed to propagate all the way to the
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
