🌐 [Leia em Português](README.pt-BR.md) | **English**

# NXProject with GitHub Projects

Since version 1.2, NXProject also works against GitHub Projects (Projects v2) — import, TaskBoard
and sync, with no Azure DevOps involved.

- [Back to the main page](../../README.md)
- [State and decisions behind the connector](features.md) — for whoever maintains it

---


From **version 1.2** NXProject also works against **GitHub Projects (Projects v2)**, not only Azure
DevOps. Teams that live on GitHub — and have neither a DevOps licence nor the appetite for one —
get the same thing: schedule with calculated dates, resource allocation and a board that respects
the backlog hierarchy. GitHub Projects is free on public and private repositories, personal or
organization, so the cost of adopting NXProject drops to zero for them.

### What works against GitHub

| | |
|---|---|
| **Import** | Whole board or a sub-tree from a root issue; hierarchy from sub-issues, dates and hours from project fields, sprint from the iteration field |
| **TaskBoard** | Columns come from the project's own `Status`, not from a fixed set |
| **Sync back** | Dates, hours, percentage, issue state (100% closes, resuming reopens), assignee, labels, new items and **backlog order** |
| **Hierarchy** | GitHub validates nothing, so NXProject does: an item outside Project → Epic → Feature → Story → Task is refused **before** anything is written |
| **Concurrency** | `Sync_version` mirrors the server's own revision (`updatedAt`), so an edit made **in GitHub's own UI** is detected too — and the warning names who did it |
| **Blocked-time audit** | Rebuilt from the issue timeline (label in/out, with time and author) |
| **Attachments** | GitHub has no item-attachment API: the file is versioned in the issue's repository, under a folder tree that mirrors the backlog, with the link posted on the item |

### Fields: NXProject creates them for you

NXProject needs a few fields to store planning data. **You do not have to create them by hand on
either side:**

- **GitHub Projects** — in NXProject, open **Import → Configure Fields and Tags → GitProject tab**
  and press **Create board fields**. It creates what is missing with the correct type (number for
  hours, date for dates, single-select for `card_type`), plus your classification fields. Creating
  a field on GitHub does **not** require organization-admin rights — only access to the project.
- **Azure DevOps** — the **NXProject-Setup** installer detects what is missing in the process and
  creates it (this one does require *Project Collection Administrator*, because it touches the
  process). The same screen inside NXProject lets you rename any field to match what your
  organization already uses.

Both sides share one field catalogue and one level map, so a field or type renamed in your
organization is a configuration change, not a code change.

### Open the fields screen directly (command line)

NXProject takes a parameter that opens **only** the "Configure Fields and Tags" screen, without
loading a schedule. This is how NXProject-Setup forwards anyone configuring GitProject — the
installer has no second screen for it, and field configuration lives in one place.

```
NXProject.Community.exe --setup            # the screen, with both tabs
NXProject.Community.exe --setup=devops      # straight to the Azure DevOps tab
NXProject.Community.exe --setup=gitproject  # straight to the GitProject tab
```

The application opens just that window and exits when it closes. In the installer, the
**"Open in NXProject"** button (Step 4, beside the server choice) makes exactly this call, already
on the selected target.


### Token for GitHub

A **classic** token with the `repo` and `project` scopes. The configuration screen explains the
scope tree, the two common mistakes (`repo:invite` is not enough; fine-grained tokens do not expose
the Projects permission for personal-account boards) and the organization alternative that avoids
granting code access. The token is encrypted by Windows (DPAPI), never stored in plain text.

> **Azure DevOps processes:** tested in production with **Agile**. Scrum and CMMI are handled in
> the code (order via `BacklogPriority`, the `Product Backlog Item` and `Requirement` types, the
> `Approved`/`Committed`/`Proposed` states), but have not yet been exercised against a real
> server. Details in [Azure DevOps features](../devops/features.md#processes-agile-is-what-has-been-tested).

## Screenshots

> The images in this section have not been taken yet. Files go under `docs/gitproject/img/` with
> exactly these names, and show up here as soon as they exist.

| Screen | File | What to show |
|---|---|---|
| Configure GitProject | `conexao.png` | Owner, token, board and repository, with the "What the token must grant" block expanded |
| Configure Fields and Tags → GitProject | `campos.png` | The board field list and the "Create the fields on the GitHub board" panel |
| TaskBoard on GitProject | `taskboard.png` | The board with columns coming from the project's `Status` |
| Import | `importar.png` | The import screen with the GitProject target selected |
| Sync result | `sync.png` | The report, preferably with a warning (backlog order or a conflict) |
