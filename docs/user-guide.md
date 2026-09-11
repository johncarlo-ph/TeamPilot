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
| Create tickets, assign agents, link branches, trigger pipeline runs | ✓ | ✓ | ✓ |
| Request changes or resolve a conflict on a review | ✓ | ✓ | ✓ |
| **Approve** a ticket (merges its branch) | ✓ | ✓ | — |
| Create or edit projects | ✓ | — | — |
| Manage users, roles, and project assignment | ✓ | — | — |
| View the audit log | ✓ | — | — |

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

Opening a project shows its board: four columns — **To Do**, **In Progress**, **For Review**,
**Done** — holding cards for that project's tickets (title, linked branch once it has one, last
updated). The board polls for changes roughly every 8 seconds, so a teammate's update appears
without a manual refresh.

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
| To Do | In Progress | Opens **Assign Agents** — the ticket only moves once at least one agent is assigned. |
| In Progress | For Review | Moves immediately, no dialog. |
| For Review | Done | Opens **Submit Review** pre-set to *Approve*. |
| For Review | In Progress | Opens **Submit Review** pre-set to *Request Changes*. |

A review can also be opened directly from the ticket's own page — see [Reviews](#reviews).

**Assign Agents** — available to anyone assigned to the project.

| Field | Type | Required | Notes |
|---|---|---|---|
| Agent checklist | Checklist | At least 1 | One checkbox per active agent in the project, with its role shown beside its name. **Assign** stays disabled until at least one is checked. If the project has no active agents yet, the dialog says so — create one under [Agents & instructions](#agents--instructions). |

## Ticket detail

Clicking a card opens its full page: title, description, and status badge at the top, then four
panels. This page also polls for updates, roughly every 10 seconds.

**Agent assignments & commits** — lists every agent assigned to the ticket and when. While the
ticket is In Progress, each assignment has an **Execute** button, which sets the agent working
and produces commits — each one pushed to the remote as soon as it's made. The Commits panel
lists every commit the ticket has picked up (short hash, message, branch, line-by-line diff).

### Git

A ticket does its work on a Git branch, cut from the project's [base branch](#projects) and
pushed to the project's connected remote as soon as it's linked. This panel links that branch
and compares any two branches in the project's repository.

| Field | Type | Required | Notes |
|---|---|---|---|
| Branch name | Text | Required to link | e.g. `feature/my-branch`, paired with **Link Branch**. Only shown before a branch is linked — a ticket can only ever have one linked branch. Creates the branch from the project's base branch and pushes it to the remote right away, so it's visible there immediately. |
| Source | Text | No | Branch to diff from, for the comparison below. |
| Target | Text | No | Branch to diff against — defaults to the ticket's own linked branch if left blank. **View Diff** lists every changed file with its patch. |

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
agents with role and Active/Inactive status; selecting a row edits that agent's instructions on
the right. Only Active agents appear in the board's Assign Agents dialog — deactivate an agent
rather than trying to delete it.

**New Agent**

| Field | Type | Required | Notes |
|---|---|---|---|
| Name | Text | Yes | How the agent appears in assignments and on the board. |
| Role | Select | Yes | Orchestrator, Research, Design, Coding, or Testing. |
| Configuration | JSON | No | Raw JSON passed to the agent's runtime configuration; blank uses defaults. |

**Editing instructions** — each agent has three instruction fields: **Constitution**,
**Guideline**, and **Requirement** — the standing text given to an agent before it works. A new
agent starts with a generic, technology-agnostic default for each field based on its role (shown
as version 1, authored by "System"); edit and save any field to add project- or
technology-specific instructions on top of that default. Each field shows its current version
number and, if there's history, a disclosure listing every prior version with author and date.

> **Saving creates a new version.** **Save & Ingest** never overwrites — it adds a new version
> for every field that was changed and leaves the others untouched. A field left blank is
> skipped; it does not create an empty version.

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

`/admin/audit-log` — Admin only. A read-only, paged record of authentication events (not ticket
or project activity).

| Column | Shows |
|---|---|
| Event | `LoginSucceeded`, `LoginFailed`, `Logout`, `TokenRefreshed`, or `TokenReuseDetected`. |
| User | The account the event happened on, when known. |
| Detail | Extra context for the event, if recorded. |
| IP Address | Where the request came from. |
| Timestamp | When it happened. |

**Previous** / **Next** page through the log; there's no search or date filter.

> **`TokenReuseDetected`** means a refresh token was presented twice — a sign a session may
> have been copied or stolen. TeamPilot responds by revoking that user's entire session family,
> forcing a fresh sign-in everywhere.

## Good to know

- **Nothing updates instantly.** The board and ticket-detail pages poll every 8–10 seconds
  rather than pushing updates live.
- **Nothing can be deleted.** Projects, tickets, and agents can be created and edited (agents
  can also be deactivated), but not deleted through the UI.
- **A ticket keeps one branch for life.** Once linked in the Git panel, it can't be swapped.
- **Approving merges code.** It's the one review decision with a real, irreversible side
  effect. Request Changes, Resolve Conflict, and comments just record a decision.
- **New sign-ins start with nothing.** A first-time sign-in has zero roles and zero project
  assignments — see [Admin: Users](#admin-users).
- **A project's remote URL is set once.** It can't be edited after the project is created — get
  it right the first time, or create a new project if it needs to point somewhere else.
- **Everything pushes to the remote as it happens.** Linking a branch, an agent's commit, and an
  approval's merge each push immediately — there's no local-only staging step, and no "sync"
  button to press.
