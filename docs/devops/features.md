🌐 [Leia em Português](features.pt-BR.md) | **English**

# Azure DevOps in NXProject — what it does and the decisions behind it

> Counterpart of [GitHub Projects](../gitproject/features.md). This is the connector NXProject has
> had from the start: what it reads, what it writes back, and the rules that are not obvious in the
> code — especially the ones that exist so as **not** to damage someone else's data.
>
> The step-by-step for **using** it lives in two places, and they do not repeat each other: inside
> the application, under **Help → Features Guide** (the *Azure DevOps* group), and in the
> [page for this target](README.md). This document is for whoever **changes** the connector.

## Features and state

| Feature | State | Note |
|---|---|---|
| Import the hierarchy from a root work item | **Done** | Project → Epic → Feature → Story → Task, with Task optional |
| Read the plan | **Done** | Estimated hours, completed hours, dates, allocation and completion percentages, assignee, state, tags, sprint |
| Backlog order | **Done** | `StackRank` (Agile/CMMI) or `BacklogPriority` (Scrum), depending on the process |
| TaskBoard | **Done** | States, card drag, item creation, moving a Task between Stories, attachments, comments |
| Sync back | **Done** | Fields, state, item creation, reparent, order, milestones |
| Blocked-time audit | **Done** | Rebuilt from the work item revisions (`/updates`) |
| Attachments | **Done** | The work item's own attachment API |
| Setup: detect and create fields | **Done** | Through NXProject's fields screen, which the installer opens |
| Setup: level report | **Done** (1.2) | Says whether the five levels have a type and which types ended up with none |
| Configurable level map | **Done** (1.2) | Type names used to be fixed in the code |
| Board virtualization | **Missing** | The TaskBoard does not virtualize; cost grows with the number of rows |
| A test for missing string keys | **Missing** | The current one catches duplicate keys, not missing ones |

## Rules that exist so as not to damage someone else's data

These are the decisions that cost the most when someone "simplifies" them without knowing why —
each has a test in `NXTestUnit`:

1. **Start date is written only if TFS already has one; finish only if the item is closed.**
   NXProject plans, but the team executes: overwriting the real date with the planned one would
   erase what actually happened.
2. **The finish written is the INCLUSIVE one.** The engine stores an exclusive finish; writing that
   would make every activity end a day later.
3. **Concurrency through `Sync_version`.** An item changed by someone else after your import
   becomes a conflict and is **not** written — not even the fields you changed on your side.
   Resolving is a human decision, on the conflicts screen.
4. **`Adm_NX` is checked LIVE**, re-reading the group from the work item; the flag stored in the
   file may be stale. An empty field allows everyone, and a technical failure (network, scope)
   does **not** block: turning a read failure into a work blocker would be worse than the problem.
5. **A duplicate name under the same parent is refused.** Without that, the next import cannot tell
   which item is which.
6. **A duplicate internal ID blocks the sync**, for the same reason — and the error names the
   activity.
7. **A Task is only written under a Story.** DevOps would refuse anyway; refusing earlier gives a
   better message. The exception is the NX milestone, which may sit outside a Story.
8. **`EPIC_TYPE = EPIC_GROUP`** is the only exception to the hierarchy: an EPIC that groups EPICs
   (for example, improvements opened per sprint). Above it only the Work Item Project, below it
   only EPICs, and a group does not nest inside another group. In the schedule it consolidates like
   the Work Item Project; on the TaskBoard it appears above the EPIC name, with a checkbox that
   suppresses all of them at once.
9. **A rank fits BETWEEN its neighbours** (`BacklogOrderService.NextRank`) instead of rewriting the
   whole backlog: touching one item does not change anyone else's rank.

## Message language

Screens use `AppStrings` and the `Strings.*.xaml` files, which live in **NXProject.Community**.
Services live in **NXProject.Shared** and see none of that — it was across that boundary that every
import and sync warning ended up hard-coded in Portuguese, even for people using the application in
English.

`NxText` closes the boundary, with both versions side by side at the call site:

```csharp
NxText.T("#{0} não está neste board.", "#{0} is not on this board.", numero)
```

It is deliberately not a key catalogue: a service message is born next to the code that produces
it, and moving it to a distant file is exactly what made the two drift apart. With the text right
there, whoever changes the rule sees both versions and cannot forget one. Keys remain the right
answer for the interface, which has thousands of them.

**State:** the GitProject services are converted. `TfsImportService` (the Azure DevOps connector)
still has ~47 messages hard-coded in Portuguese — that is the next batch.

## Processes: Agile is what has been tested

NXProject has been exercised **in production with the Agile process**. Scrum and CMMI are **handled
in the code**, but have not been run against a real server — and the difference between those two
statements matters:

| Piece | How all three processes are covered |
|---|---|
| Backlog order | Scrum writes `BacklogPriority`; Agile and CMMI write `StackRank`. And before that, whichever field the work item **actually has** wins |
| Story type | `User Story` (Agile), `Product Backlog Item` (Scrum) and `Requirement` (CMMI) are factory names in the level map |
| States | Open: `New`, `Active`, `In Progress`, `To Do`, `Approved` (Scrum), `Proposed` (CMMI), `Committed`. Closed: `Closed`, `Resolved`, `Done`, `Completed` |
| TaskBoard columns | They come from the states found in the items, ordered by a canonical list that already includes Scrum vocabulary. An unknown state goes last, never disappears |
| Hours, dates, percentages | They are NXProject's **own** fields, created by the installer — they do not depend on the process |

**The known caveat:** with the **EN** naming standard, NXProject uses the native fields
(`Original Estimate`, `Completed Work`). They exist on the Agile and CMMI Task; on **Scrum** the
out-of-the-box Task has `Remaining Work`, and the other two may not exist. With the **PT** standard
(NXProject's own fields) this does not happen, and "Detect fields" warns before any import.

Since type and field names are configurable, adapting to another process is **configuration, not a
code change**.

## Fields: the catalogue is the single source

`NxDevOpsFieldCatalog` describes each NXProject concept (hours, dates, percentages, sync, approval,
admin group, blocking) with a Portuguese name, an English name, the work item types where it
applies and the equivalent **native** field, when there is one.

- **The PT standard** uses NXProject's own fields (`Data_Inicio`, `HH Estimado`).
- **The EN standard** uses the process's **native** field whenever the type has one (`Start Date`,
  `Finish Date`, `Original Estimate`), and the English name when it does not.
- The rule that avoids the damage: in the English standard, **only what HAS a native field in that
  type becomes native**. Without it, a Task would end up with two start dates — the native one and
  NXProject's.

The same catalogue serves NXProject and NXProject-Setup. There were once three diverging copies of
that rule between the two, and each copy became a defect.

## Levels: the type name is configuration, not code

Until version 1.2, levels were recognized by fixed names in the code (`epic`/`epico`,
`feature`/`funcionalidade`, `project`/`projeto`). Anyone who named the type "Initiative", "Program"
or "Product" was left out without recompiling.

Today that is `NxLevelMap`: accepted names per level, a level that can be switched off (a team
without Project starts at Epic; without Task, the Story is the leaf) and parent→child validation
skipping whatever is disabled. It applies to both targets, and the **Configure Fields and Tags →
Levels** screen edits it and detects what exists on the server.

## The installer forwards, it does not duplicate

The "Configure Fields and Tags" screen exists **once**, in NXProject. NXProject-Setup opens it from
the command line instead of carrying a copy:

```
NXProject.Community.exe --setup              # both tabs
NXProject.Community.exe --setup=devops       # Azure DevOps tab
NXProject.Community.exe --setup=gitproject   # GitProject tab
```

The application opens that window only and exits when it closes. Two screens for the same
configuration is exactly what made the field catalogue drift between the two applications — three
times.

## Permissions: two of them, and they get confused

| For what | What it requires |
|---|---|
| Reading and writing work items (normal NXProject use) | A PAT with the work item scope |
| **Creating a field** in the process | *Project Collection Administrator* — it changes the organization's process |

The fields screen asks for the admin PAT **at the moment of creation**, uses it only there and does
not store it: the day-to-day token neither has nor should have that scope.

## Differences from GitHub Projects

A summary for whoever knows one side and is about to touch the other — the detail is in
[GitHub Projects](../gitproject/features.md):

| | Azure DevOps | GitHub Projects |
|---|---|---|
| Fields | of the work item, per type | of the **board** (project) |
| Hierarchy | validated by the server | **nobody validates**: NXProject does |
| Order | a numeric field | position in the view (one call per item, ~2s each) |
| Concurrency | server `Rev` + `Sync_version` | `updatedAt` mirrored into `Sync_version` |
| Who may sync | an identity group in `Adm_NX` | an organization team **or** a list of logins |
| Attachment | the item's attachment API | a file versioned in the repository + a link on the item |
| Creating a field | requires process admin | access to the project is enough |
