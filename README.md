🌐 [Leia em Português](README.pt-BR.md) | **English**

---

# NXProject Community

**Management visibility over Azure DevOps — without changing anything in the technical team's workflow.**

NXProject lets Tech Leads, Scrum Masters, Project Managers, and Business Stakeholders see the real project picture straight from Azure DevOps: schedule, dependencies, resource allocation, and Gantt chart — in a free Windows desktop application.

The development team keeps working in Azure DevOps exactly as before: code traceability, pull requests, pipelines, and delivery quality remain untouched. NXProject reads that data and turns the backlog into a planning view that managers and leads can actually use to make decisions.

---

## The problem NXProject solves

IT projects using Azure DevOps have an organized backlog, defined sprints, and updated work items — but **management has no integrated schedule view**. Simple questions go unanswered:

- When will this Feature be done, considering all its Stories?
- Which resource is overloaded next month?
- If this Story slips, what else gets impacted?
- Is the project going to deliver on time?

NXProject imports the Azure DevOps hierarchy and turns that data into a manageable schedule, with Gantt, dependencies, allocation, and delay alerts — **without requiring the technical team to change anything in their process**.

---

## Every role sees what they need, without friction

The development team keeps using Azure DevOps as the single source of truth: linked commits, code review, pipeline automation, and full traceability remain intact. NXProject is a **read-and-planning layer** on top of that data, aimed at those who need to answer questions about deadlines, capacity, and risk.

---

## The story behind NXProject

After more than 20 years technically leading data products and managing software engineering projects, I kept running into the same silent conflict: technical tools were great for the development team but didn't deliver the visibility management needed. Management tools gave executives the reports and Gantt charts they asked for, but penalized the technical team with parallel processes, rework, and lost traceability.

The solution was always an unsatisfying compromise — either the team suffered, or management stayed in the dark.

After a long time searching for a way out, I decided to build something different: a tool that doesn't force anyone to choose between technical rigor and management visibility. One that respects the team's Azure DevOps workflow while giving the manager the schedule, dependencies, and alerts needed to make decisions with confidence.

That's how NXProject was born — a new way to manage IT projects, where the technical and the managerial work together, without friction and without trade-offs.

---

## Download

- [Download NXProject Community ZIP with `.exe` and DLLs](../../releases/latest/download/NXProject.Community-Release.zip)
- [View release notes and source code downloads](../../releases/latest)

**No installation required.** Extract the ZIP and run `NXProject.Community.exe` — the .NET runtime is bundled inside.

> The binary was built in an environment with McAfee antivirus. If you prefer to build from source, see the instructions below.
>
> Release note: the official NXProject Community `.exe` must be generated with `dotnet publish --self-contained true`, so the release package includes the .NET runtime and runs directly through `NXProject.Community.exe`.
> If the package is generated only with `dotnet build`, Windows may show a misleading corrupted .NET, broken installation, or missing runtime message.

---

## Screenshots

![NXProject Community main screen](ScreenShot/Tela01.png)
![Hierarchy and Gantt view](ScreenShot/Tela02.png)
![Configuration and tracking](ScreenShot/Tela03.png)
![TFS / Azure DevOps import](ScreenShot/Tela04.png)
![Azure DevOps Backlog concept](ScreenShot/Tela05-Azure-DevOps-Backlog.svg)

---

## Who is NXProject for

| Role | What NXProject delivers |
|---|---|
| **Project Manager** | Schedule integrated with the backlog, delay alerts, dependency view |
| **Scrum Master / RTE** | Capacity per sprint, allocation conflicts, impact of date changes |
| **Tech Lead** | Features and Stories with predecessors and hour-based estimates |
| **PMO** | Multi-project consolidation, export to MS Project / Excel |

---

## Azure DevOps Integration

### From backlog to schedule in minutes

NXProject imports the full hierarchy of your project directly from Azure DevOps:

```
Project → Epic → Feature → Story
```

Each Story becomes a schedule row with start date, working-day duration, assignee, and sprint — all extracted from the fields your team already fills in DevOps.

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
- **Allocation %**: `Perc_Alocação` — how much of the person's day is dedicated to this Story (affects finish date)
- **Sync version**: `Sync_version` and `Sync_Name` — concurrency control (see below)

> Field names can be changed in the **Advanced fields** section of the import dialog if your process uses different names.

---

### Required custom fields on the Story work item type

NXProject reads and writes six custom fields on Stories in Azure DevOps. You must create them in your process template under **Organization Settings → Process → [Your Process] → Story → Fields**.

| Field name (display) | Reference name | Type | Default in NXProject | Purpose |
|---|---|---|---|---|
| `HH Estimado` | `Custom.HHEstimado` *(example)* | Integer or Decimal | `HH Estimado` | Estimated effort in hours |
| `Data_Inicio` | `Custom.DataInicio` *(example)* | Date/Time | `Data_Inicio` | Planned start date |
| `Data_Fim` | `Custom.DataFim` *(example)* | Date/Time | `Data_Fim` | Planned finish date |
| `Perc_Alocação` | `Custom.PercAlocacao` *(example)* | Integer (1–100) | `Perc_Alocação` | % of person's day dedicated to this Story |
| `Sync_version` | `Custom.Syncversion` *(example)* | Integer | `Sync_version` | Concurrency version counter (auto-managed) |
| `Sync_Name` | `Custom.SyncName` *(example)* | Text *(plain text, not Identity)* | `Sync_Name` | Who last synced (auto-managed) |

> The reference names above are examples — Azure DevOps generates them automatically from the display name and your organization prefix.  
> If your fields have different display names, update them in NXProject under **File → Import TFS / Azure DevOps → Advanced fields** expander.

#### Concurrency control (`Sync_version` / `Sync_Name`)

When two users sync changes simultaneously, the last write could overwrite the first. NXProject prevents this:

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

## Usability

- **Interactive Gantt chart** with zoom by day, sprint, or custom period
- **Task dependencies** (predecessors), including across Stories from different Epics
- **Resource allocation**: workload view per person and period
- **Project Health Check**: lists delayed tasks and tasks with no assignee
- **Configurable calendar**: holidays, working hours per day, weekdays
- **Export**: MS Project XML, OpenProj, Excel XML, CSV, **PDF (landscape)**
- **AI Assistant** for task structure suggestions
- **Multilingual**: Portuguese (Brazil) and English, auto-detected from Windows, switchable in Settings

---

## Build from source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [VS Code](https://code.visualstudio.com/download).

```powershell
# Set up environment
.\setup-community-vscode.ps1

# Build
.\build-community.ps1 -Configuration Release

# Or generate the self-contained distribution zip
.\release-community-new-version.ps1 -Configuration Release
```

The development executable will be generated in `NXProject.Community\bin\Release\net10.0-windows\`.

> **Important — generating the official release `.exe`**
>
> Always use `dotnet publish --self-contained true -r win-x64` (or the project's release script, which already does this).
> If you use only `dotnet build`, the resulting `.exe` may fail on machines with a broken .NET registry entry, showing a misleading error such as:
>
> ```
> To run this application, you must install .NET.
> ```
>
> …even when `dotnet --list-runtimes` shows .NET installed correctly.
> The self-contained package bundles the .NET runtime inside the ZIP and avoids this issue entirely.

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

## License and contact

- **Company**: Nexus XData Tecnologia Ltda
- **Commercial contact**: `comercial.nexus.xdata@gmail.com`

NXProject uses an **Open Core / dual licensing** model:

| Edition | Use |
|---|---|
| **Community (free)** | Free for individuals and companies, including internal commercial use, unlimited users. Free redistribution allowed with credit to Nexus XData. |
| **Commercial / Enterprise** | No restrictions on resale or SaaS, official support, SLA, exclusive modules. Contact us for a proposal. |

> Selling, charging for, or offering NXProject as a paid service requires a commercial license.

---

## Tell us how NXProject is helping your project

If NXProject is being used at your company and making a difference — whether in schedule visibility, team management, or Azure DevOps integration — **we want to hear about it**.

Send a short message to `comercial.nexus.xdata@gmail.com` with:

- Project context (team size, industry, the challenge you had)
- What improved after you started using NXProject
- If you authorize it, we'll share the case as a reference for the community

Real testimonials help prioritize improvements, attract contributors, and show other teams that the product works in practice. **Your experience can help other projects.**
