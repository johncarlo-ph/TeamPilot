# User Guide (`TeamPilot.UI`)

[← Back to README](../README.md)

## Purpose

A field-by-field walkthrough of every page in the TeamPilot web app, for end users (as opposed
to [docs/frontend.md](frontend.md), which documents the Angular app's architecture for
developers). Whenever a change adds, removes, or renames a page, field, button, or role
restriction in `TeamPilot.UI`, update the matching section below in the same change — this
doc drifting out of sync with the UI is the failure mode it exists to prevent.

## Overview

TeamPilot tracks tickets on a Kanban board, hands the work to AI agents that commit against a
real Git repository, and routes every change through a human approval gate before it counts as
done. Everything lives inside one **project** — a project owns its own Git repository, its own
pool of agents, its own ticket board, and its own pipeline-run history — so every screen below
belongs to whichever project is currently open.

## Signing in

There is no username/password form. The sign-in screen offers a **Sign in with Google**
button and/or a **Sign in with Microsoft** button, whichever the deployment has configured.
Sign-in happens in the provider's own popup; TeamPilot never sees your password, only a
verified token handed back by Google or Microsoft.

You stay signed in across reloads and browser restarts until you use **Sign out** (top-right of
the navbar) or your session is revoked — the app refreshes your session silently in the
background.

> **Before you start:** the very first time an account signs in, it has **no role and no
> project assigned** — only the account that bootstraps the system (`Auth:SeedAdminEmails`)
> starts as Admin. A new sign-in that sees an empty app needs an Admin to open
> [Users](#admin-users) and give it a role and at least one project.

## Roles & access

Every account holds one or more of three roles, and separately is assigned to one or more
projects — an Admin sets both from [Users](#admin-users). Project assignment decides *which*
projects an account can see at all; role decides what it's allowed to do once it's looking at
one. Every rule below is enforced server-side, not just hidden in the UI — a control a user
can't use is either not shown, or shown disabled with an explanation next to it.

| Capability | Admin | Developer | Analyst |
|---|---|---|---|
| View the board and tickets of an assigned project | ✓ | ✓ | ✓ |
| Create tickets, start the agent pipeline, link branches, trigger pipeline runs | ✓ | ✓ | ✓ |
| Edit an agent's instructions | ✓ | ✓ | ✓ |
| Request changes or resolve a conflict on a review | ✓ | ✓ | ✓ |
| **Approve** a ticket (merges its branch) | ✓ | ✓ | — |
| Add/remove/reorder pipeline agents, add a custom agent, configure a loop-back | ✓ | — | — |
| Create or edit projects | ✓ | — | — |
| Manage users, roles, and project assignment | ✓ | — | — |
| View the audit log | ✓ | — | — |
| Manage instruction templates | ✓ | — | — |

## Projects

`/projects` — the landing page after sign-in. One card per project the account is assigned to,
showing name, description, the connected remote repository URL, its sprint dates/goal when set,
and a row of count badges — one per board column (⏳ To Do, 🔧 In Progress, 🚫 Blocked,
👀 For Review, ✅ Done) - so you can see where a project's tickets stand without opening its board,
with an **Open Board** button.

**New Project / Edit Project** — Admin only; the button and each card's Edit/Remove links are
hidden for Analysts and Developers. Before saving — whether creating a new project or editing an
existing one's base branch — the entered base branch is checked against the remote repository
itself; if it doesn't exist there, the save is rejected outright with an error and nothing
changes (unlike a bad URL/token, below, this check happens before anything is saved). On edit,
that check reuses the project's already-stored access token unless a new one was also entered.
Once the check passes, creating a project saves it immediately and starts cloning its remote
repository in the background — the card shows a live progress bar (with an object/byte count
once Git reports one) while cloning, and **Open Board** stays disabled until it finishes. A bad
URL or token doesn't fail the creation itself; instead the card switches to a "Clone failed"
banner with the error, and the project stays listed (not silently discarded) so the Admin can fix
the details and re-create it.

**Remove** — Admin only, next to **Edit** on each card. Hides the project from this list; it does
**not** delete anything — the connected Git repository, its branches, and every ticket stay
untouched, and there's no way to un-hide it from the UI afterward. Disabled (with an explanatory
tooltip) while the project has any ticket **In Progress** or **For Review**; clicking it asks for
confirmation, since it can't be undone from the UI.

| Field | Type | Required | Notes |
|---|---|---|---|
| Name | Text | Yes | Display name — shown on the project card and the board header. |
| Description | Text | No | Shown on the card; blank shows "No description." |
| Remote URL | Text | Yes (create only) | The `https://` URL of the Git repository to connect, e.g. `https://github.com/org/repo.git`. Can't be changed after the project is created — shown as read-only text when editing. |
| Access Token | Password | Yes on create, optional on edit | A Personal Access Token for that repository, with permission to read and write it. On edit, leave blank to keep the currently stored token (e.g. after rotating it on the host, paste the new one). Never shown again once saved. |
| Base branch | Text | Yes (defaults to `main` on create) | Every ticket's branch is cut from here, and an approved ticket's branch is merged back into it. Must already exist on the remote repository, whether creating or editing — the save is rejected with an error otherwise. |
| Sprint start date | Date | No | For framing this project as an Agile sprint. Purely informational — nothing in the app gates on it. |
| Sprint end date | Date | No | Same as above. Rejected if set earlier than the sprint start date (when both are given). |
| Sprint goal | Text (multi-line) | No | The sprint's objective, free text. |

## Ticket board

Opening a project shows its name at the top (so it's never ambiguous which project you're
looking at) above a two-panel view: the [Live Agent chat](#live-agent-chat) on the left, and the
board itself on the right — five color-coded columns — ⏳ **To Do**, 🔧 **In Progress**,
🚫 **Blocked**, 👀 **For Review**, ✅ **Done** — holding cards for that project's tickets (title, linked branch
once it has one, last updated). Clicking anywhere on a card, including its branch-name line,
opens that ticket's detail page — the branch name itself is plain text here; it only becomes a
clickable link to the remote once you're on the [ticket detail](#ticket-detail) page. The board
polls for changes roughly every 8 seconds, so a teammate's update — including a ticket you just
approved from the chat panel — appears without a manual refresh.

The chat panel has a small round arrow button in its header that collapses it down to a thin
strip, giving the board itself the extra width — handy on a smaller screen or a project with a
lot of columns. The collapsed strip shows the same button, now a 💬 icon; click it to expand the
chat back to full size. Both the collapse and the chat panel's own height animate smoothly, and
the chat panel always stretches to fill the space down to the bottom of the window.

### Live Agent chat

Every project also has a **Live Agent** — a chat you can ask about the project itself: general
questions, explanations of code in the project's repository, the project's business rules, and
questions about its existing tickets. It only reads the repository (nothing it does can change,
move, or delete a file), and it only looks at files and tickets relevant to what you actually
asked.

A project can have any number of **chat sessions** with the Live Agent, and everyone with access
to the project sees the same list and can switch between them from the dropdown at the top of the
chat panel — each entry shows the session's name and who started it. Click **+ New chat** to start
your own session (it starts out named "New chat"); click the ✏️ button next to the dropdown to
rename whichever session is currently selected — any project member can rename any session, not
just the one they started. Within a session, each message is labeled with the name of the person
who sent it (the Live Agent's own replies are labeled **Live Agent**).

If you ask it to create, log, or file a ticket, it drafts one — title, description, and
acceptance criteria — as a card right in the chat, with **Create ticket** and **Reject** buttons
side by side. Before drafting, it checks the board's existing tickets and will call out related or
duplicate ones in the draft when relevant. Not happy with the wording? Click the ✏️ button on the
card to edit the title, description, and acceptance criteria right there before creating it —
**Create ticket** then uses your edited text instead of the original draft. Nothing is created
until you click **Create ticket**; the
Live Agent never adds a ticket to the board on its own. Once you approve it, the new ticket appears on the
board (in To Do) the next time the board polls, just like one you created yourself with
**New Ticket**, and the card shows a **Ticket created** badge instead of the buttons. If the
draft isn't what you wanted, click **Reject** instead — nothing is created, and the card shows a
**Ticket rejected** badge, so the buttons don't stay there inviting an accidental click later.
Either way, the badge sticks even after you reload the page or if a teammate looks at the same
chat, so nobody can approve or reject the same draft twice.

**New Ticket** — available to anyone assigned to the project. New tickets always start in To Do.

| Field | Type | Required | Notes |
|---|---|---|---|
| Title | Text | Yes | Shown on the card and at the top of the ticket's detail page. |
| Description | Text (multi-line) | No | The work to be done — what an assigned agent reads to know what to build. |
| Acceptance Criteria | Text (multi-line) | Yes | The condition(s) this ticket must satisfy to be considered done — the pipeline agents (especially Research and Design) use this to scope their work, and it's what the human approval gate ultimately checks against. Shown on the ticket detail page. |

### Moving a ticket

Drag a card between columns to move it. There's no free-form status dropdown — each drop maps
to one specific action, and an unsupported drop (e.g. To Do straight to Done) is rejected
client-side with an explanation.

| From | To | What happens |
|---|---|---|
| To Do | In Progress | Starts the ticket immediately, no dialog — see "The agent pipeline" below. |
| In Progress | For Review | Moves immediately, no dialog. |
| For Review | Done | Opens **Submit Review** pre-set to *Approve*. |
| For Review | In Progress | Opens **Submit Review** pre-set to *Request Changes* - submitting it re-runs the pipeline immediately. |

A review can also be opened directly from the ticket's own page — see [Reviews](#reviews).

**Blocked isn't a drop target.** A ticket lands there on its own — an agent asked a clarifying
question, or a Git/LLM call failed — and can only leave by answering or retrying on its own
detail page (see "Blocked tickets" below), never by dragging.

### The agent pipeline

Every new project starts with one Research, one Design, one Coding, and one Testing agent, in
that order — created automatically when the project is created, so there's nothing to set up
before starting a ticket. An Admin can customize this per project (add agents, remove them,
reorder them, add further loop-backs) on the [Agent Pipeline](#agent-pipeline--instructions)
page; what's described here is that default sequence. Dragging a card from To Do to In Progress
(or clicking **Start** on the ticket's own page) runs the project's configured agents in order,
in a single step: by default, Research investigates, Design plans, Coding implements and commits,
then Testing verifies. If the ticket doesn't already have a linked branch (i.e. **Link Branch** on
the Git panel was never used), one is created and linked automatically at this point, named after
the ticket itself. If Testing finds a problem, it's sent straight back to Coding to fix —
automatically, up to 3 attempts total — before the ticket lands in For Review either way, so it's
always ready for a human to look at. There's no per-agent "Execute" step to click through, and no
agent picker: whatever the project's pipeline is configured to run, runs every time.

If any stage needs something only a human can provide before it can continue — or a Git/LLM call
fails outright — the ticket moves to **Blocked** instead of continuing on to the next stage. See
"Blocked tickets" below.

### Blocked tickets

A ticket becomes Blocked in exactly three situations, all handled on its detail page:

- **An agent asked a clarifying question.** A stage can end its work with a question instead of a
  finished result when something is genuinely ambiguous — which library to use, which of two
  valid approaches to take, and so on. The **Blocked** panel on the ticket's page shows the
  question; type your answer and send it. The ticket unblocks and the whole pipeline runs again
  immediately, with that stage using your answer to continue (earlier, already-settled stages
  just briefly reaffirm their prior conclusion rather than redoing their work).
- **An agent raised a proceed-or-cancel decision** — for example, it noticed this ticket conflicts
  with another one already in flight. An agent can never cancel a ticket itself, so the panel
  shows a reminder above the answer box: if abandoning the ticket is the right call, use the
  **Cancel Ticket** button above instead of typing a reply. Only type an answer if the pipeline
  should continue anyway despite the conflict.
- **A known Git or LLM failure occurred** — a push was rejected, the LLM provider's API failed
  after retrying. The same panel shows the failure message and a **Retry Pipeline** button
  instead of an answer box, since there's nothing to type - retrying just runs the pipeline again
  from the start.

Every question, decision, and failure the ticket has ever hit stays listed here as a running
history, even after you've answered or retried past it — the panel doesn't disappear once the
ticket moves on. A Blocked ticket can also be **Cancel**led like any other pre-merge ticket, if
it's not worth resolving.

Not every failure blocks the ticket this way — only the two categories above (a stage asking a
question, or a known Git/LLM failure). A genuinely unexpected error still surfaces as a plain
error message with nothing to retry from the board.

## Ticket detail

Clicking a card opens its full page: title, description, acceptance criteria, and status badge at
the top, then four panels. This page also polls for updates, roughly every 10 seconds.

**Agent assignments & commits** — lists every agent assigned to the ticket and when. While the
ticket is To Do or In Progress, a **Start** (or **Run Pipeline**, once it's already In Progress)
button runs the agent pipeline described above — the same action as dragging the card, available
here for retrying a failed run or re-running after a review's *Request Changes*. Whenever a run is
actually in progress — right after you click it, or because it was triggered another way (a
*Request Changes* review, answering a question, retrying a failure) — the button is replaced with
a "Pipeline running…" note instead of staying there to be clicked again. The Commits
panel lists every commit the ticket has picked up (short hash, message, branch, line-by-line
diff) — a retried Coding attempt shows up as an additional commit, unless it's a
*Request Changes* re-run and Coding itself decides no code change is actually needed for your
feedback, in which case no new commit is added.

**Agent Log** — a live, chronological history of every agent's activity on this ticket: "Research
agent started", "Coding agent completed" (with the agent's full result shown underneath), and so
on for every stage of every pipeline run, including a stage that paused the ticket with a question
or hit an operational failure. It updates in real time while a pipeline run is in progress, so you
can watch which stage is currently working instead of only seeing the outcome once the whole run
finishes.

**Cancel Ticket** — shown next to the status badge for any To Do, In Progress, For Review, or
Blocked ticket (e.g. its goal no longer applies because a requirement changed). Asks for
confirmation, then an optional reason, and sets the ticket to **Cancelled** — permanent, with no
way back. Since a ticket's work only ever lives on its own feature branch until a human approves
it, and Cancel is only available before that approval, cancelling never touches the project's
base branch — it just stops tracking the ticket as active work. If the ticket has a linked
branch, cancelling deletes it from the remote in the same action (see [Git](#git) below), since a
Cancelled ticket drops off the board entirely (they're not a column, unlike Blocked) with no other
link back to its detail page — leaving the branch to be deleted manually later would leave it
essentially unreachable. The ticket itself stays reachable from wherever its link was shared,
showing the cancellation reason if one was given.

### Git

A ticket does its work on a Git branch, cut from the project's [base branch](#projects) and
pushed to the project's connected remote as soon as it's linked. This panel links that branch
and compares any two branches in the project's repository.

| Field | Type | Required | Notes |
|---|---|---|---|
| Branch name | Text | Required to link | e.g. `feature/my-branch`, paired with **Link Branch**. Only shown before a branch is linked — a ticket can only ever have one linked branch. Once linked, the name shown here is a link straight to that branch on the remote (opens in a new tab). |
| Source | Text | No | Branch to diff from, for the comparison below. |
| Target | Text | No | Branch to diff against — defaults to the ticket's own linked branch if left blank. **View Diff** lists every changed file with its patch. |

Clicking **Link Branch** first checks whether that name already exists in the project's
repository, then opens a confirmation dialog worded for whichever case it found — an existing
branch found (link the ticket to it) or no branch found (a new one will be created from the
project's base branch and pushed to the remote right away). Nothing is created or linked until
that confirmation is accepted.

> **A branch can only ever belong to one ticket.** If the name you enter is already linked to a
> different ticket in this project — including a Cancelled one that still has a branch linked
> (see below for why that's now the exception rather than the rule), since its old branch may
> still carry commits from before it was abandoned — linking is refused with an error naming
> that other ticket. Start a new ticket instead of trying to reuse the branch.

**Cancelling a ticket automatically deletes its linked branch** from the project's remote (and
the local copy TeamPilot keeps) in the same action — this is genuinely irreversible, unlike
everything else in this list, and it's also what frees the branch name up for reuse by another
ticket right away. If a Cancelled ticket still shows a linked branch anyway (e.g. it was
cancelled before this behavior existed, or the automatic deletion needs retrying), a
**Delete Branch** button appears next to the branch name to do it manually.

> **Approval needs a branch.** A ticket can't be approved until it has a linked branch —
> **Approve** is disabled on the review form until one exists, since approving merges that
> branch.

### Conflicts

Finds and resolves merge conflicts between the ticket's branch and its merge target.

| Control | What it does |
|---|---|
| Detect Conflicts | Scans the branch and lists conflicting files, each with its conflicting diff. |
| Suggest AI Resolution | Asks an agent to propose a fix for that conflict. |
| Accept AI Suggestion | Applies the proposed fix and marks the conflict resolved. |
| Resolution note + Resolve Manually | Optional free-text note paired with marking a conflict resolved by hand. |

> **A resolved conflict can go stale.** TeamPilot remembers what the merge target looked like
> when a conflict was resolved. If someone else's change lands on that branch before this
> ticket is approved, the resolution is checked again at approval time — if it no longer applies
> cleanly, approval fails, that conflict flips back to unresolved automatically, and you'll need
> to resolve it again (re-running **Detect Conflicts** first is the safest way to see exactly
> what changed).

### Reviews

Every decision on a ticket (approve, request changes, reject, resolve a conflict) is recorded
here with the reviewer's name, decision, and comments. While the ticket is For Review, **Submit
Review** opens the same form a board drag-and-drop opens, without presetting a decision.

**Submit Review** — anyone assigned to the project can request changes or resolve a conflict;
Approve and Reject are both Admin/Developer only, since they're the ticket's two "final" outcomes.

| Field | Type | Required | Notes |
|---|---|---|---|
| Reviewer name | Text | Yes | Pre-filled with the signed-in user's name. |
| Decision | Select | Yes | **Approve** (merges the branch into the project's base branch and pushes the merge to the remote; Admin/Developer, requires a linked branch), **Request Changes** (sends the ticket back to In Progress and immediately re-runs the pipeline - see below), **Reject** (Admin/Developer; permanently deletes the ticket's branch and cancels it - see below), or **Resolve Conflict**. |
| Comments | Text (multi-line) | No | Shown alongside the decision in the ticket's review history. For **Request Changes**, these comments are also passed to the agents on the re-run as feedback to address - write them as instructions to the agents, not just notes to yourself. |

> **Request Changes now runs the pipeline immediately** — you don't need a separate Run Pipeline
> click afterward. The whole configured sequence runs again from its first stage, and every stage
> sees your comments - each one decides for itself whether your feedback actually affects its
> part of the work: an unaffected stage (e.g. Research, when you only flagged a code-level bug)
> quickly reaffirms its earlier conclusion instead of redoing it from scratch, while the stage(s)
> your feedback actually concerns revise their work properly. Write comments as instructions to
> the agents, specific enough that they can tell what's actually affected.

> **Reject is permanent.** Unlike Request Changes, there's no path back: the ticket is cancelled
> and, if it has a linked branch, that branch (and its commits) is deleted from the remote in the
> same action. The confirmation dialog says so explicitly - use Request Changes instead if the
> ticket's work is salvageable and just needs adjusting.

## Agent Pipeline & Instructions

Reached from a project's board via the **Agents** button. The left side lists the project's
agents in the order tickets run through them; selecting one edits its instructions on the right.
The standing Live Agent isn't listed here — it's managed via the
[Live Agent chat](#live-agent-chat) instead.

**Everyone assigned to the project can view the pipeline and edit any agent's instructions**
(subject to the Constitution being fixed on default agents — see **Editing instructions** below).
Reordering the pipeline, adding or removing an agent, and configuring a loop-back are
**Admin-only** — a Developer or Analyst sees the same ordered list, but without the drag handle,
Remove button, or loop-back controls.

**Reordering** — an Admin drags a row to a new position; the new order takes effect for every
ticket started after the change (a ticket's pipeline is read fresh each time it runs).

**Adding a custom agent** is two steps: **+ Add custom agent** creates a new agent with a name
but no instructions yet, and immediately opens its instruction editor — fill in all three fields
(Constitution, Guideline, Requirement) and save. The new agent then shows up under
**Available agents (not in pipeline)** with an **Add to pipeline** button, which places it at the
end of the sequence. Trying to add it before its instructions are complete is rejected with an
explanation.

**Removing an agent** takes it out of the sequence via its **Remove** button — it stops running
on future tickets, but its instructions (and its history of past commits, if it's a Coding agent)
are kept, and it reappears under "Available agents" in case you want to re-add it later. This
applies to any agent, including the original Research/Design/Coding/Testing ones. Only a Coding
agent commits to the project's Git repository; every other agent (including any custom one) only
produces text that's passed along to the next stage.

**Deleting a custom agent** — a custom agent under "Available agents" also has its own
**Remove** button, and this one is permanent: it only appears for agents you created but that
have never actually run on a ticket (so there's nothing to lose), and clicking it asks you to
confirm before deleting the agent outright. It's unrelated to the pipeline's Remove button above
and works even while the pipeline is locked, since an agent that was never scheduled can't be
mid-run on anything. A default Research/Design/Coding/Testing agent, or any agent that has ever
run, never gets this button — those can only ever be removed from the pipeline, never deleted.

**Loop-backs** — a stage can be configured to jump back to an earlier stage when its output looks
like a failure, up to a set number of attempts before the ticket moves on to review anyway. The
default pipeline's Testing stage already does this (loops back to Coding, up to 3 attempts) — an
Admin can add the same behavior to any other stage, or change the target/attempt count, via
**+ Add loop-back** on a stage that doesn't have one yet, or **Clear** on one that does.

**The pipeline locks while any ticket is In Progress or Blocked** — a banner explains this, and
reordering, adding, removing, and loop-back changes are all disabled (with a clear error if
attempted directly) until every ticket in the project has moved past those two states. Editing an
already-scheduled agent's instructions is unaffected by this lock.

**Editing instructions** — each agent has three instruction fields: **Constitution**,
**Guideline**, and **Requirement** — the standing text given to an agent before it works. A new
agent starts with a generic, technology-agnostic default for each field based on its role (shown
as version 1, authored by "System"); edit and save a field to add project- or technology-specific
instructions on top of that default. Each field shows its current version number and, if there's
history, a disclosure listing every prior version with author and date. Above a Guideline or
Requirement field, a **Template** dropdown is always shown (greyed out if there's nothing to pick
yet) and lists any saved [instruction templates](#admin-instruction-templates) that match this
agent's role and that field's type — picking one fills the textarea with the template's content,
and the dropdown keeps showing what you picked, so you can review or tweak it before saving;
nothing is applied until you save.

> **A default agent's Constitution is fixed.** For the standing Research, Design, Coding,
> Testing, and Live Agent roles, the Constitution field is read-only (labeled **Fixed**, with no
> Template dropdown) — it defines what that role fundamentally is in the pipeline, and changing it
> is not allowed since the pipeline's own logic (e.g. Testing looping back to Coding) depends on
> each stage staying what its role says it is. Use that agent's Guideline or Requirement instead
> to adapt how it works for your project. A **custom** agent has no such restriction — its
> Constitution is yours to define and edit like any other field, since it has no built-in role to
> preserve.

> **Saving creates a new version.** **Save & Ingest** never overwrites — it adds a new version
> for every field that was changed and leaves the others untouched. A field left blank is
> skipped; it does not create an empty version.

> **Mid-pipeline edits only affect stages that haven't started yet.** A warning banner on this
> page spells this out: if you edit an instruction while this agent's ticket is already running,
> it applies to any stage that hasn't started yet — including a Coding retry after a Testing
> failure — but a stage already in progress will finish using whatever instructions were current
> when it started.

## Admin: Users

`/admin/users` — Admin only; the navbar shows **Users** and **Audit Log** links only to Admins.
A table of every account (name, email, roles, Active/Disabled status); **Edit** opens a user's
settings.

**Edit User**

| Field | Type | Notes |
|---|---|---|
| Roles | Checklist | Admin / Analyst / Developer — a user can hold more than one. Applies immediately on **Save Roles**; this is the only way a brand-new account gets any access at all. |
| Assigned Projects | Checklist | Which projects this user can see and work in. Saved separately via **Save Projects**. |
| Account status | Toggle | **Disable Account** / **Enable Account** — a disabled account can no longer sign in; nothing is deleted. |

## Admin: Audit log

`/admin/audit-log` — Admin only. A read-only, paged record of authentication events and every
state-changing action taken elsewhere in the app (creating or updating a project, ticket, or
agent; submitting a review; changing a user's roles; and so on).

| Column | Shows |
|---|---|
| Event | The kind of thing that happened — e.g. `LoginSucceeded`, `TicketCreated`, `ReviewSubmitted`, `UserRolesChanged`. See `AuditEventType` for the full list. |
| User | The name of the account that performed the action, when known (not a raw account id). |
| Detail | Extra context for the event, if recorded. |
| IP Address | Where the request came from. |
| Timestamp | When it happened. |

**Previous** / **Next** page through the log; there's no search or date filter.

> **`TokenReuseDetected`** means a refresh token was presented twice — a sign a session may
> have been copied or stolen. TeamPilot responds by revoking that user's entire session family,
> forcing a fresh sign-in everywhere.

> A silent background token refresh is **not** logged — it happens automatically on a timer, not
> as the result of anything a user did, so it would only add noise between the login and logout
> entries that actually matter.

## Admin: Instruction Templates

`/admin/instruction-templates` — Admin only. A catalog of reusable instructions, independent of
any one project, meant for conventions you reuse across repeating projects (e.g. a standard TDD
guideline for Coding agents, or a security checklist for Testing agents). This is where you
build up that catalog; picking one for a real agent happens on the
[Agent Pipeline & Instructions](#agent-pipeline--instructions) page, from the **Template**
dropdown next to the matching field.

| Field | Type | Notes |
|---|---|---|
| Name | Text | How it appears in the table and in the **Template** dropdown. |
| Agent Role | Select | Research, Design, Coding, or Testing — which role's instruction editor this template shows up in. Can't be changed after creation; create a new template instead. |
| Instruction Type | Select | Constitution, Guideline, or Requirement — which field it shows up under. Also fixed after creation. |
| Content | Text (multi-line) | Copied verbatim into the instruction editor's textarea when picked — nothing is saved until the admin then clicks **Save & Ingest** there. |

**Edit** changes a template's Name and Content in place (no version history — it's a starting
point, not an audited record). **Delete** removes it permanently; templates already applied to a
real agent are unaffected, since applying one just copies its content into that agent's own
versioned instructions at the time.

## Good to know

- **Nothing updates instantly.** The board and ticket-detail pages poll every 8–10 seconds
  rather than pushing updates live.
- **Almost nothing can be deleted.** Projects, tickets, and agents can be created and edited
  (agents can also be deactivated), but not deleted through the UI. A project can be **Removed**
  instead (see [Projects](#projects)) — that only hides it from the list; its repository, branches,
  and tickets are untouched. A ticket can be **Cancelled** instead (see
  [Ticket detail](#ticket-detail)) — that's a permanent status, not a deletion; the ticket and its
  history stay on record. Instruction templates and a Cancelled ticket's Git branch are the two
  real exceptions — both genuinely, irreversibly deleted, the branch automatically as part of
  cancelling (or manually via its **Delete Branch** button, for a ticket
  cancelled before this was automatic). An agent's **Remove** button on the
  [Agent Pipeline](#agent-pipeline--instructions) page looks like a third exception but usually
  isn't: it only takes the agent out of the pipeline sequence — the agent itself, and its
  instructions and commit history, are untouched and it can be added back later. The one genuine
  exception is a custom agent that's never actually run — its "Available agents" **Remove**
  button really does delete it permanently, since there's nothing on record to lose.
- **A ticket keeps one branch for life — with one exception.** Once linked in the Git panel, it
  can't be swapped for a different one. Deleting the branch — automatically when the ticket is
  Cancelled or Rejected (see [Reviews](#reviews)), or manually for a ticket cancelled before that
  was automatic — is the only way that field goes back to empty, and even then a Cancelled ticket
  can't link a new one — it's terminal either way.
- **Cancelling, Approving, and Rejecting all have real, irreversible side effects.** Cancelling
  and Rejecting both delete the ticket's branch; Approve merges code instead. Request Changes,
  Resolve Conflict, and comments just record a decision (though Request Changes does trigger a
  new pipeline run).
- **New sign-ins start with nothing.** A first-time sign-in has zero roles and zero project
  assignments — see [Admin: Users](#admin-users).
- **A project's remote URL is set once.** It can't be edited after the project is created — get
  it right the first time, or create a new project if it needs to point somewhere else.
- **Everything pushes to the remote as it happens.** Linking a branch, an agent's commit, and an
  approval's merge each push immediately — there's no local-only staging step, and no "sync"
  button to press.
