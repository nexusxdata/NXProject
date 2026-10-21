🌐 [Leia em Português](features.pt-BR.md) | **English**

# GitHub Projects in NXProject — features, state and decisions

> This document started as two design plans. The connector was built: TaskBoard, import, sync,
> backlog order, blocked-time audit, assignee and attachments all work against a real board.
>
> What is left here is what the code does **not** tell: what was measured instead of assumed, what
> was deliberately not done and why, and where the result contradicted the original plan. Without
> it, the next person redoes the same measurements to reach the same conclusions.

## Why it exists

NXProject was born talking only to Azure DevOps. Teams that live on GitHub — with neither a DevOps
licence nor the appetite for one — needed exactly the same: a schedule with calculated dates,
resource allocation and a board that respects the backlog hierarchy. GitHub Projects is free on
public and private repositories, personal or organization, so for those teams the cost of adopting
NXProject drops to zero.

## Features and state

Everything below is **done** — what is missing and what will not be done have their own sections
at the end.

| Feature | Where |
|---|---|
| Import (hierarchy from sub-issues, board fields, sprints, state, predecessors from "blocked by") | `GitProjectImportService` |
| Discovery (list boards, find the portfolio) | `GitProjectDiscovery` |
| TaskBoard, with columns coming from the project's own `Status` | `GitProjectBoardService` |
| Board save (state, title, hours, dates, assignee, labels, sprint, reparent) | `GitProjectBoardWriter` |
| Sync (fields, issue state, item creation, assignee, predecessors as "blocked by") | `GitProjectSyncService` |
| Backlog order | `BacklogOrderService` (the rule) + `INxOrderWriter` (how each target writes it) |
| Concurrency: `Sync_version` mirroring the server revision | `GitProjectSyncService.Revision` |
| Who may sync (`Adm_NX`): organization team or list of logins | `GitProjectAdminService` |
| Blocked-time audit, from the issue timeline | `GitProjectBlockAuditService` |
| Attachments: file versioned in the repository + link on the item | `GitProjectAttachmentService` |
| Creating the missing board fields | "Configure Fields and Tags" → *Create board fields* |
| Configurable level map, valid for **both** targets | `NxLevelMap` |
| `EPIC_TYPE = EPIC_GROUP`: an EPIC grouping EPICs (board field) | `NxLevelMap.ParentChildOk`, on import and sync |

## What was measured, not assumed

The probes live outside the normal test suite (they write to a real server) and give the board back
exactly as they found it:

```
dotnet run --project NXTestUnit -c Release -- gitorder   # backlog order
dotnet run --project NXTestUnit -c Release -- gitpoc     # BLOCK, attachments and assignee
```

| Question | What the plan assumed | What the measurement showed |
|---|---|---|
| Does backlog order come back? | 🔴 high risk: "order does not come back" | **It does.** `updateProjectV2ItemPosition` works at the top and in the middle, and the next read reflects it. The problem is not fragility, it is **cost**: ~2s per move, 98s to rewrite a queue of 48 items. Hence writing order is a **diff** — only what is out of place gets moved |
| Can we have predecessors? | not available | **Yes.** GitHub's native dependency ("blocked by", `addBlockedBy`/`removeBlockedBy`) is the counterpart of the DevOps Dependency-Reverse link: "#A blocked by #B" = B is a predecessor of A. Sync honours the same `SyncPredecessorLinks` switch and only touches blocks between issues **on the board** — a block from outside becomes an import warning and is never deleted. Measured with a round trip on the test board |
| Can blocked time be audited? | "may never be the same" | **It can.** `timelineItems` with `LABELED_EVENT`/`UNLABELED_EVENT` returns the label, the time and the actor of every in and out — the same data `/updates` gives in DevOps, in another shape |
| Can a file be attached to an item? | attachment "in the comment body" | **There is no item-attachment API.** GitHub's own screen path (upload to a CDN) is not automatable. The substitute is versioning the file in the issue's repository |
| Can the assignee be synced? | "the mutation is enough" | The mutation is trivial; the problem is **identity**. 6 of 100 accounts in a public repository had no display name, and a name is not unique. Solved by storing the **login** on the resource during import |
| Who changed it last? | our own stamp (`Sync_Name`) | The server already knows: the `creator` of the most recent field value, or the issue's `editor`. Better than the stamp — that one would only see writes made by NXProject itself |

## Decisions that changed the original design

1. **Phase 1 was not done as written.** The plan asked to extract `IWorkItemProvider` from the 337
   calls to `TfsImportService`. What exists is `INxBoardSource`, an interface for what the board
   consumes only. The reason was risk: rewriting 33 files before knowing whether the new connector
   was worth it would be paying both bills up front. **The honest consequence:** import and sync
   still live in two places, and that is the risk that costs the most over time.
2. **The field creator ended up in NXProject, not in the installer.** Creating a field on GitHub
   does **not** require organization-admin rights, unlike DevOps — so it belongs on the screen of
   whoever uses it, next to the detector.
3. **The level map came before the rest**, and it fixed a limitation of the **DevOps** connector:
   `IsBoardProjectType`, `IsBoardEpicType`, `IsBoardFeatureType`, `IsStoryType` and `IsTaskType`
   read fixed names in the code, leaving out anyone who named the type "Initiative" or "Product".
4. **GitProject classification is born as a copy of the DevOps one.** A picklist becomes a
   single-select with the same values — on GitHub only single-select has a closed list.
5. **`Adm_NX` got two shapes.** Teams only exist in organizations; a personal-account board has
   none. So the field accepts `@org/team` **or** a comma-separated list of logins.

## What will not be done, and why

| | Reason |
|---|---|
| **A GitProject tab in NXProject-Setup** | It would duplicate connection and fields in a second place. The installer exists for the pain GitHub does not have (Process API, *Project Collection Administrator*), and NXProject already creates board fields. Instead of a second screen, the installer **calls** this one: `NXProject.Community.exe --setup=gitproject` |
| **Deleting an issue from the board** | Deleting is irreversible and requires admin rights. The save warns and suggests closing the item |
| **Priority on the GitHub board** | It is not an item field there. Whoever needs it creates a number field on the board |

## What GitHub forces you to accept

- **A field belongs to the BOARD, not to the item.** Moving the issue to another project takes
  hours and dates away with it.
- **Nobody validates hierarchy.** A Task under a Feature is possible from GitHub's own screen; the
  validation is NXProject's — a warning on import (the schedule is where you fix it), a refusal on
  sync.
- **A label belongs to the repository.** A board gathering issues from several repositories needs
  the same label in each one — which is why NXProject creates it before applying.
- **A repository attachment is versioned forever.** Deleting leaves the Git history. It is no place
  for personal data — unlike a DevOps attachment, which is gone when removed.
- **Sub-issue limits:** ~8 levels and ~100 children per parent.

## Token

Classic, with **`repo`** (the issues, including in a private repository) and **`project`** (read and
write the board). Two mistakes that cost time and are now explained on the configuration screen
itself:

- `repo:invite` is a **child** of `repo` and only covers collaboration invitations — it grants
  access to no issue at all. Checking only that one produces a board that reads the fields and
  **no** items;
- a *fine-grained* token does not work for a **personal-account** board: it does not expose the
  Projects permission. In an organization it does — and there you can grant only *Issues* +
  *Projects*, without code access.

## Still missing

- **Board virtualization** (applies to all three targets, not only GitProject).
- **A test for missing string keys**: the current one catches duplicate keys, not missing ones.
