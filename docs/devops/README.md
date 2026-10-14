🌐 [Leia em Português](README.pt-BR.md) | **English**

# NXProject with Azure DevOps

How NXProject turns an Azure DevOps backlog into a managed schedule — what it reads, what it
writes back, and how to set it up.

- [Back to the main page](../../README.md)
- [State and decisions behind the connector](features.md) — for whoever maintains it

---

## Azure DevOps Integration

### From backlog to schedule in minutes

NXProject imports the full hierarchy of your project directly from Azure DevOps:

```
Project → Epic → Feature → Story
```

Each Story becomes a schedule row with start date, working-day duration, assignee, and sprint — all extracted from the fields your team already fills in DevOps.

### How far the schedule goes

NXProject plans down to the **Story** level; the Task is born and run in the TaskBoard. On Azure
DevOps you can pull Tasks into the schedule (**Load Task ToDo**) when your process asks for it. The
reasoning behind the split is in
[Degrees of freedom](../../README.md#degrees-of-freedom-the-schedule-stops-at-the-story-the-task-lives-in-the-taskboard),
on the main page.

### TaskBoard: where execution happens

The schedule answers "when"; the **TaskBoard** shows "who is doing what, right now". It reads the sprint Tasks straight from Azure DevOps and lays them out in columns by state (New, Active, Resolved, Closed), in two views:

- **Project & Story** — Stories in the state columns, grouped by Project, EPIC and Feature. The manager's tracking view.
- **Person & Task** — one band per person and, inside it, one row per Story with the Tasks spread across the columns (the illustration above). The team's day-to-day view.

Beyond listing, the board lets you:

- **Drag a Task across columns to change its state**; nothing goes to DevOps right away — changes are queued and written in a single batch through the **Update TFS** button, with a report of what changed.
- **Doing / Done**: marks for what a person is working on now and for what is finished but not yet closed in DevOps (they become work item tags).
- **WIP limit per person** (not per project): the board counts each person's Tasks in progress and flags whoever went over the limit.
- **Inconsistent state alert**: the Story is highlighted when its state does not follow the Tasks — a Story in New with a Task already started, or a Story in Active with no Task in Active.
- **Blocking (`BLOCK` tag)**, sprint, assignee, priority, estimated/completed hours and description, all editable from the card.
- **Filters** by person, Story, state and sprint (one or many), plus slices such as "blocked only", "Active tasks only" and "schedule stories only".

The TaskBoard is where the degree of freedom described above becomes concrete: planning delivers the Story, and the team creates and drives the Tasks from there.

### DevOps Project List

Manage multiple DevOps projects in a shared file across your team. Each project has a name and root ID; to import, just pick the project from the list — no need to remember the ID manually.

### What is read automatically

- **Hierarchy**: `Project → Epic → Feature → Story` via `Child` links
- **Estimates**: custom `HH Estimado` field → duration in working days on the project calendar
- **Dates**: `Data_Inicio` and `Data_Fim` when already set in DevOps
- **Assignee**: `System.AssignedTo` → project resource
- **Sprint**: `System.IterationPath` → sprint association in NXProject
- **Backlog order**: `Microsoft.VSTS.Common.StackRank`
- **Blockers**: child Tasks with the `Block` tag mark the Story as blocked
- **State**: `Closed`/`Resolved` Stories with open child Tasks are flagged and auto-corrected
- **Allocation %**: `Perc_Alocacao` — how much of the person's day is dedicated to this Story (affects finish date)
- **Sync version**: `Sync_version` and `Sync_Name` — concurrency control (see below)

> Field names can be changed in the **Advanced fields** section of the import dialog if your process uses different names.

---

### Root work item and hierarchy (`Project` type)

NXProject builds the schedule on the **Project → Epic → Feature → Story → Task** hierarchy.

> ⚠️ **`Project` is NOT a standard Azure DevOps work item type** (the standard tops out at Epic). It's a **custom** type that acts as a "container" above Epics, grouping the whole project. Many organizations create this type in their process.

How the root is used:

- **Manual import:** you enter the **root work item ID** in the import screen; NXProject imports the descendants (Epic → Feature → Story → Task). The root type does **not** have to be exactly `Project` — it can be any work item that is the parent of the Epics (even an Epic, if you only want to import that one).
- **Discovery** (Portfolio → Discovery DevOps): automatically lists work items **of type `Project`** with no parent in the Team Project. For automatic Discovery to work, the custom `Project` type must exist.

If your organization doesn't use a `Project` type, you can still import by pointing the root ID at an Epic (or other container) — only automatic Discovery depends on the `Project` type.

> **Fields on the `Project` type:** since it sits at the top of the hierarchy, create the **same custom fields on it as on the Epic** (`Estimated HH`, `Data_Inicio`, `Data_Fim`, `Sync_version`, `Sync_Name`). In practice the `Project` type is usually a copy of the Epic. NXProject reads the project start date (`Data_Inicio`) directly from the root item.

---

### DevOps fields: what is required and what is optional

NXProject reads and writes a few custom fields in Azure DevOps. Create them in your process template under **Organization Settings → Process → [Your Process]** and add them to the work item types listed under **Used on**.

> **Before creating them by hand, use NXProject-Setup.** **Step 4** of the installer detects what already exists in your organization, **creates only what is missing**, and can **export/import** the configuration as `.json` for the whole team. The same detection lives in NXProject under **Azure DevOps Integration Settings → Advanced fields**; creation is exclusive to the installer.

> **Use the built-in fields when you can.** Azure DevOps already ships `Start Date` and `Finish Date` on **Task** and **Story**, and `Original Estimate` on Task. NXProject uses the Task ones by default and **offers the same for the Story**: tick *"on Story use Start Date / Finish Date"* (in either screen) and you **do not need to create** `Data_Inicio`/`Data_Fim`. The choice is stored per work item type, so teams already using the custom fields keep working as before.

#### Required

| Field name (display) | Reference name | Type | Default in NXProject | Used on | Purpose |
|---|---|---|---|---|---|
| `HH Estimado` | `Custom.HHEstimado` *(example)* | Decimal | `HH Estimado` | Story *(Task uses the built-in `Original Estimate`)* | Estimated effort in hours |
| `Data_Inicio` | `Custom.DataInicio` *(example)* | Date/Time | `Data_Inicio` | Story, Task — **or** the built-in `Start Date` | Planned start date |
| `Data_Fim` | `Custom.DataFim` *(example)* | Date/Time | `Data_Fim` | Story, Task — **or** the built-in `Finish Date` | Planned finish date (target date on the TaskBoard) |

#### Optional

Each one enables a feature. Without the field NXProject still works — only that feature is left out.

| Field name (display) | Reference name | Type | Default in NXProject | Used on | Purpose |
|---|---|---|---|---|---|
| `Perc_Alocacao` | `Custom.PercAlocacao` *(example)* | Decimal/Float (1–100, up to 2 decimals) | `Perc_Alocacao` | Story, Task | % of person's day dedicated to this Story |
| `Perc_Conclusao` | `Custom.PercConclusao` *(example)* | Integer (0–100) | `Perc_Conclusao` | Story | % completion. **Without the field**, the percentage comes from the work item state |
| `EPIC_TYPE` | `Custom.EPIC_TYPE` *(example)* | Text (list: `DELIVERY` / `BACKLOG` / `EPIC_GROUP`) | `EPIC_TYPE` | Epic | Classifies the Epic: **Delivery** (adds hours to the project total), **Backlog** (does not) or **EPIC_GROUP** (an Epic that groups Epics). Enabled by default. |
| `Type` *(classification)* | `Custom.Type` *(example)* | Picklist | `Custom.Type` | Feature | Feature classification, shown on the schedule and the TaskBoard. Factory values: `Architecture`, `Burocracy`, `Docs`, `Feature`, `Hotfix`, `Refactor`, `Request`. Both the field and the list are configurable in **Configure Fields and Tags**, and the same list seeds the single-select board field on GitProject. |
| `Tipo_Centro_Custo` | `Custom.Tipo_Centro_Custo` *(example)* | Text (`OPEX` / `CAPEX`) | `Tipo_Centro_Custo` | Epic | Cost-center type — used by the **Projects Portfolio** (OPEX/CAPEX) |
| `block_duration_hours` | `Custom.block_duration_hours` *(example)* | Decimal | `block_duration_hours` | Task | Hours accumulated in BLOCK, computed from history. **Off by default** |
| `Approved` | `Custom.Approved` *(example)* | Boolean | `Approved` | Task | Task approval. Enabled by default |
| `Sync_version` | `Custom.Syncversion` *(example)* | Integer | `Sync_version` | Story, Feature, Epic | Concurrency version counter (auto-managed) |
| `Sync_Name` | `Custom.SyncName` *(example)* | Text *(plain text, not Identity)* | `Sync_Name` | Story, Feature, Epic | Who last synced (auto-managed) |
| `Adm_NX` | `Custom.Adm_NX` *(example)* | Identity (points to a DevOps group/Team) | `Adm_NX` | Project (root item) | NX admin group: **only members of this group can Export/Sync** to DevOps. Empty/missing = open to everyone. Enabled by default. |

> **Optional HH fields (advanced).** Besides `HH Estimado`, NXProject recognizes separate hour fields when they exist, to preserve the plan on 100%-complete items: `HH Original` (`HH_Original_float`), `HH Restante` (`HH_Restante_float`) and `HH Atual` (`HH_Atual_float`) — on Story, Feature and Epic. If absent, NXProject derives the values from `HH Estimado`/state.

> The reference names above are examples — Azure DevOps generates them automatically from the display name and your organization prefix.  
> If your fields have different display names, set them in NXProject under **Configure Azure DevOps → Advanced fields**, where all field names are configurable: HH Estimado, Data_Inicio, Data_Fim, Perc_Alocacao, Perc_Conclusao, EPIC_TYPE, Tipo_Centro_Custo, Sync_version, Sync_Name, Adm_NX and block_duration_hours.

> **Don't want to guess the names? Use "Detect DevOps fields".** At the end of **Advanced fields** there is a button that asks DevOps which of the names you typed actually exist, **in which work item types** (Task, User Story, Feature, Epic, Project) and with which data type. It also reports when a name was matched by an **alternative name** — NXProject accepts a few per field, so `HH Estimado` still works when the field is really called `Esforço Estimado` — and it ticks or unticks the optional-feature checkboxes according to what it found. Nothing is written to DevOps and nothing is saved until you press **Save**; **Cancel** undoes it.
>
> Detection only answers whether the field exists and where. Process rules, write permission and incompatible data types only surface when NXProject actually writes — if an import or sync fails for that reason, the error message names the field.

> **Admin group (`Adm_NX`) — who can sync.** On the root (`Project`) work item, create an **Identity** field named `Adm_NX` and point it to a DevOps **group/Team**. Only the **members of that group** can Export/Sync to DevOps from the imported schedule; local editing and Task Plan stay free for everyone. The group is shown in the banner and in the **Project Portfolio** editor. Who may sync is validated **live** at Sync time — NXProject **re-reads the `Adm_NX` field straight from the `Project` work item in DevOps** and compares it to the authenticated user (the PAT owner), without relying on the config checkbox or on what was cached in the `.nxp`; so turning the option off after import does **not** turn the gate off. Empty/missing field on the `Project` work item = **open to everyone**. It replaces the old Portfolio "Read-only" flag. Configurable (enable/name) under **Advanced fields**.
>
> **Important:** after creating the `Adm_NX` field in the process *template*, you must **open each `Project` work item, pick the group and Save**. Until the value is persisted on the item, the field is empty in the API — even if it shows in the form — and NXProject reads it as "open to everyone".

> **Backlog order (StackRank / BacklogPriority):** NXProject writes the backlog order to the process's standard field — `Microsoft.VSTS.Common.StackRank` on **Agile/CMMI/Basic** and `Microsoft.VSTS.Common.BacklogPriority` on **Scrum**. These fields already exist in the process (no need to create them). The Team Project process is read automatically on import/discovery and shown in the banner and the Portfolio editor.

> **Tip:** create the fields once at the process level and then add them to Story, Feature and Epic work item types — they share the same field definition across types.

#### Task fields (no custom fields required)

The **Task** uses only **standard** Azure DevOps fields, which already exist on the Task type — you do **not** need to create any custom field:

| NXProject concept | Standard field (reference) | Note |
|---|---|---|
| Estimated HH | `Microsoft.VSTS.Scheduling.OriginalEstimate` | Task estimated effort |
| Current HH | `Microsoft.VSTS.Scheduling.CompletedWork` | Completed work |
| Priority | `Microsoft.VSTS.Common.Priority` | The stock DevOps form uses 1–4; in NXProject the range is configurable (default 1–9) |
| Backlog order | `Microsoft.VSTS.Common.StackRank` / `BacklogPriority` | Process's standard field (no need to create) |
| Assigned To / State / Activity | `System.AssignedTo` / `System.State` / `Microsoft.VSTS.Common.Activity` | — |

> **`Approved` field (optional, Task only).** If your process has a boolean `Approved` field (`Custom.Approved`) on the Task, NXProject reads and writes the Task approval. Enabled by default; ignored if the Task lacks the field. Configurable under **Advanced fields**.

> **`block_duration_hours` field (optional, Task).** A numeric field that stores **how many hours the
> item was impeded** (blocked with the BLOCK tag), always in whole hours — under one hour becomes `0`.
> NXProject recalculates the total from the DevOps history when you unblock, and the Task card shows a
> ⏱ shortcut to the **BLOCK audit** when the value is greater than zero.
> **Disabled by default and entirely optional**: leave the checkbox off — under **Advanced fields** —
> and NXProject never writes to it. Blocking keeps working with the tag plus a comment, and the BLOCK
> audit (right-click on a Story or Task card) still reads the full history online, with or without this
> field. Nothing in the process template is required.

> Dates, `Perc_Alocacao`, `EPIC_TYPE`, `Tipo_Centro_Custo` and `Sync_version`/`Sync_Name` do **not** apply to Tasks — planning (dates and duration) is derived from the parent Story.

#### Concurrency control (`Sync_version` / `Sync_Name`)

When two users sync changes simultaneously, the last write could overwrite the first. NXProject prevents this with the `Sync_version` / `Sync_Name` pair, which must be present on every work item type you sync (Story, Feature, Epic):

- On every sync that writes at least one change, `Sync_version` is incremented by 1 and `Sync_Name` is set to the current Windows user.
- When you sync, NXProject compares the version it read during import with the current version in DevOps. If the DevOps version is higher, someone else saved more recently — the item is **skipped** and marked in **red** in the schedule.
- Red items remain highlighted until you re-import the project. The sync log shows which items had conflicts.
- Clicking a red item in the state column opens the DevOps link window, which displays a conflict warning with a **↓ Re-import** button to start the import directly.
- The version counter resets to 1 after reaching the integer limit.

> **`Sync_Name` must be plain text, not Identity type.** If you created it as an Identity (person picker) field, delete it and recreate it as **Text (single line)**.

### Import log

When importing, NXProject generates a report with:
- Stories whose state was auto-corrected (e.g., closed Story with open Task)
- Predecessors pointing to items outside the imported scope
- Warnings and inconsistencies to review before publishing the schedule

### Sync back to DevOps

After adjusting dates, dependencies, and estimates in the schedule, NXProject syncs the changes back to Azure DevOps: title, description, hours, dates, state, tags, sprint, and predecessor links.

### Open work items directly in DevOps

On any linked task, the **"Open in DevOps ↗"** button opens the work item in the browser. The link window also shows the list of child Tasks with ID, name, and state — for quick reference without leaving NXProject.

---

---


## Configure Azure DevOps

### Personal Access Token

1. In Azure DevOps, click the user icon → **Personal access tokens**
2. Click **New Token**
3. Under **Scopes**, select **Work Items → Read** (add **Write** if you want to sync back)
4. Copy the token and paste it in the import screen in NXProject

The token can be saved locally encrypted with Windows credentials (DPAPI).

### Working calendar

Configure holidays, working hours per day, and weekdays under **View → Calendar...**  
Default is 8 hours per day, Monday through Friday.

---

## The fields screen

![Configure Fields and Tags, Azure DevOps tab](img/campos.png)

The field names in DevOps, the PT/EN standard and, per work item type, the option to use the
process's **native** field instead of a field of our own. Below the list sit detection and
creation — the latter asking for the admin PAT on the spot, without storing it.
