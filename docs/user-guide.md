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
| Create or edit projects | ✓ | — | — |
| Manage users, roles, and project assignment | ✓ | — | — |
| View the audit log | ✓ | — | — |
| Manage instruction templates | ✓ | — | — |

## Projects

`/projects` — the landing page after sign-in. One card per project the account is assigned to,
showing name, description, and the connected remote repository URL, with an **Open Board**
button.

**New Project / Edit Project** — Admin only; the button and each card's Edit link are hidden
for Analysts and Developers. Creating a project clones its remote repository right away, so the
request can take a few seconds and fails if the URL or token is wrong.

| Field | Type | Required | Notes |
|---|---|---|---|
| Name | Text | Yes | Display name — shown on the project card and the board header. |
| Description | Text | No | Shown on the card; blank shows "No description." |
| Remote URL | Text | Yes (create only) | The `https://` URL of the Git repository to connect, e.g. `https://github.com/org/repo.git`. Can't be changed after the project is created — shown as read-only text when editing. |
| Access Token | Password | Yes on create, optional on edit | A Personal Access Token for that repository, with permission to read and write it. On edit, leave blank to keep the currently stored token (e.g. after rotating it on the host, paste the new one). Never shown again once saved. |
| Base branch | Text | Yes (defaults to `main`) | Every ticket's branch is cut from here, and an approved ticket's branch is merged back into it. |

## Ticket board

Opening a project shows its name at the top (so it's never ambiguous which project you're
looking at) above a two-panel view: the [Live Agent chat](#live-agent-chat) on the left, and the
board itself on the right — four color-coded columns — ⏳ **To Do**, 🔧 **In Progress**,
👀 **For Review**, ✅ **Done** — holding cards for that project's tickets (title, linked branch
once it has one, last updated). Clicking anywhere on a card, including its branch-name line,
opens that ticket's detail page — the branch name itself is plain text here; it only becomes a
clickable link to the remote once you're on the [ticket detail](#ticket-detail) page. The board
polls for changes roughly every 8 seconds, so a teammate's update — including a ticket you just
approved from the chat panel — appears without a manual refresh.

### Live Agent chat

Every project also has a **Live Agent** — a chat you can ask about the project itself: general
questions, explanations of code in the project's repository, and the project's business rules.
It only reads the repository (nothing it does can change, move, or delete a file), and it only
looks at files relevant to what you actually asked.

If you ask it to create, log, or file a ticket, it drafts one — title and description — as a
card right in the chat, with a **Create ticket** button. Nothing is created until you click that
button; the Live Agent never adds a ticket to the board on its own. Once you approve it, the new
ticket appears on the board (in To Do) the next time the board polls, just like one you created
yourself with **New Ticket**.

**New Ticket** — available to anyone assigned to the project. New tickets always start in To Do.

| Field | Type | Required | Notes |
|---|---|---|---|
| Title | Text | Yes | Shown on the card and at the top of the ticket's detail page. |
| Description | Text (multi-line) | No | The work to be done — what an assigned agent reads to know what to build. |

### Moving a ticket

Drag a card between columns to move it. There's no free-form status dropdown — each drop maps
to one specific action, and an unsupported drop (e.g. To Do straight to Done) is rejected
client-side with an explanation.

| From | To | What happens |
|---|---|---|
| To Do | In Progress | Starts the ticket immediately, no dialog — see "The agent pipeline" below. |
| In Progress | For Review | Moves immediately, no dialog. |
| For Review | Done | Opens **Submit Review** pre-set to *Approve*. |
| For Review | In Progress | Opens **Submit Review** pre-set to *Request Changes*. |

A review can also be opened directly from the ticket's own page — see [Reviews](#reviews).

### The agent pipeline

Every project always has one Research, one Design, one Coding, and one Testing agent — they're
created automatically when the project is created, so there's nothing to set up before starting
a ticket. Dragging a card from To Do to In Progress (or clicking **Start** on the ticket's own
page) runs all four, in that fixed order, in a single step: Research investigates, Design plans,
Coding implements and commits, then Testing verifies. If the ticket doesn't already have a
linked branch (i.e. **Link Branch** on the Git panel was never used), one is created and linked
automatically at this point, named after the ticket itself. If Testing finds a problem, it's sent
straight back to Coding to fix — automatically, up to 3 attempts total — before the ticket lands
in For Review either way, so it's always ready for a human to look at. There's no per-agent
"Execute" step to click through anymore, and no agent picker: the same four roles run every time.

## Ticket detail

Clicking a card opens its full page: title, description, and status badge at the top, then four
panels. This page also polls for updates, roughly every 10 seconds.

**Agent assignments & commits** — lists every agent assigned to the ticket and when. While the
ticket is To Do or In Progress, a **Start** (or **Run Pipeline**, once it's already In Progress)
button runs the agent pipeline described above — the same action as dragging the card, available
here for retrying a failed run or re-running after a review's *Request Changes*. The Commits
panel lists every commit the ticket has picked up (short hash, message, branch, line-by-line
diff) — a retried Coding attempt shows up as an additional commit.

**Cancel Ticket** — shown next to the status badge for any To Do, In Progress, or For Review
ticket (e.g. its goal no longer applies because a requirement changed). Asks for confirmation,
then an optional reason, and sets the ticket to **Cancelled** — permanent, with no way back.
Since a ticket's work only ever lives on its own feature branch until a human approves it, and
Cancel is only available before that approval, cancelling never touches the project's base branch
or remote history — it just stops tracking the ticket as active work. Cancelled tickets drop off
the board entirely (they're not a 5th column) but stay reachable from wherever their link was
shared, showing the reason if one was given.

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
> different ticket in this project — including a Cancelled one, since its old branch may still
> carry commits from before it was abandoned — linking is refused with an error naming that
> other ticket. Start a new ticket instead of trying to reuse the branch.

Once a ticket is **Cancelled**, a **Delete Branch** button appears next to the linked branch
name. Confirming it permanently deletes that branch from the project's remote (and the local
copy TeamPilot keeps) — this is genuinely irreversible, unlike everything else in this list.
It's the one way to actually free up a branch name for reuse by another ticket; leaving a
cancelled ticket's branch alone (the default — nothing deletes it automatically) keeps that name
permanently reserved to that ticket.

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

### Reviews

Every decision on a ticket (approve, request changes, resolve a conflict) is recorded here with
the reviewer's name, decision, and comments. While the ticket is For Review, **Submit Review**
opens the same form a board drag-and-drop opens, without presetting a decision.

**Submit Review** — anyone assigned to the project can request changes or resolve a conflict;
Approve is Admin/Developer only.

| Field | Type | Required | Notes |
|---|---|---|---|
| Reviewer name | Text | Yes | Pre-filled with the signed-in user's name. |
| Decision | Select | Yes | **Approve** (merges the branch into the project's base branch and pushes the merge to the remote; Admin/Developer, requires a linked branch), **Request Changes** (sends the ticket back to In Progress), or **Resolve Conflict**. |
| Comments | Text (multi-line) | No | Shown alongside the decision in the ticket's review history. |

## Agents & instructions

Reached from a project's board via the **Agents** button. The left table lists the project's
four pipeline agents, always in pipeline order (Research, Design, Coding, Testing) with name and
role; selecting a row edits that agent's instructions on the right. The standing Live Agent isn't
listed here — it's managed via the [Live Agent chat](#live-agent-chat) instead. They're created
automatically for every project — there's no way to create, rename, or delete an agent here,
only to reconfigure or edit an existing one.

**Editing instructions** — each agent has three instruction fields: **Constitution**,
**Guideline**, and **Requirement** — the standing text given to an agent before it works. A new
agent starts with a generic, technology-agnostic default for each field based on its role (shown
as version 1, authored by "System"); edit and save any field to add project- or
technology-specific instructions on top of that default. Each field shows its current version
number and, if there's history, a disclosure listing every prior version with author and date.
Above each field, a **Template** dropdown is always shown (greyed out if there's nothing to pick
yet) and lists any saved [instruction templates](#admin-instruction-templates) that match this
agent's role and that field's type — picking one fills the textarea with the template's content,
and the dropdown keeps showing what you picked, so you can review or tweak it before saving;
nothing is applied until you save.

> **Saving creates a new version.** **Save & Ingest** never overwrites — it adds a new version
> for every field that was changed and leaves the others untouched. A field left blank is
> skipped; it does not create an empty version.

> **Mid-pipeline edits only affect stages that haven't started yet.** A warning banner on this
> page spells this out: if you edit an instruction while this agent's ticket is already running,
> it applies to any stage that hasn't started yet — including a Coding retry after a Testing
> failure — but a stage already in progress will finish using whatever instructions were current
> when it started.

## Pipeline runs

Also reached from the board. Tracks the status of a project's CI/CD runs — Queued, Running,
Succeeded, Failed — as a log of when runs started and finished. It does not run a real build.

| Control | What it does |
|---|---|
| Trigger reason (text, optional) + Trigger Run | Records free text (e.g. "manual re-run") against the new run being created. |
| Start | Moves a Queued run to Running. |
| Mark Succeeded / Mark Failed | Closes out a Running run with its final status. |

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
agent; submitting a review; triggering a pipeline run; changing a user's roles; and so on).

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
build up that catalog; picking one for a real agent happens on the [Agents & instructions](#agents--instructions)
page, from the **Template** dropdown next to the matching field.

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
  (agents can also be deactivated), but not deleted through the UI. A ticket can be **Cancelled**
  instead (see [Ticket detail](#ticket-detail)) — that's a permanent status, not a deletion; the
  ticket and its history stay on record. Instruction templates and, once a ticket is Cancelled,
  its Git branch are the two real exceptions — both genuinely, irreversibly deleted when you
  click their **Delete** button.
- **A ticket keeps one branch for life — with one exception.** Once linked in the Git panel, it
  can't be swapped for a different one. Deleting a Cancelled ticket's branch (see
  [Ticket detail](#ticket-detail)) is the only way that field goes back to empty, and even then
  a Cancelled ticket can't link a new one — it's terminal either way.
- **Approving merges code.** It's the one review decision with a real, irreversible side
  effect. Request Changes, Resolve Conflict, and comments just record a decision.
- **New sign-ins start with nothing.** A first-time sign-in has zero roles and zero project
  assignments — see [Admin: Users](#admin-users).
- **A project's remote URL is set once.** It can't be edited after the project is created — get
  it right the first time, or create a new project if it needs to point somewhere else.
- **Everything pushes to the remote as it happens.** Linking a branch, an agent's commit, and an
  approval's merge each push immediately — there's no local-only staging step, and no "sync"
  button to press.
