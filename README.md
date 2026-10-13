🌐 [Leia em Português](README.pt-BR.md) | **English**

---

# NXProject Community

**Management visibility over Azure DevOps and GitHub Projects — without changing anything in the technical team's workflow.**

> **New in 1.2:** besides Azure DevOps, NXProject now imports, boards and syncs against
> **GitHub Projects**. See [GitHub Projects integration](#github-projects-integration-new-in-12).

NXProject lets Tech Leads, Scrum Masters, Project Managers, and Business Stakeholders see the real project picture straight from Azure DevOps: schedule, dependencies, resource allocation, and Gantt chart — in a free Windows desktop application.

The development team keeps working in their own server exactly as before: code traceability, pull requests, pipelines, and delivery quality remain untouched. NXProject reads that data and turns the backlog into a planning view that managers and leads can actually use to make decisions.


> Documentation per target, with what is read, what is written back and how to set it up:
> [Azure DevOps](docs/devops/README.md) · [GitHub Projects](docs/gitproject/README.md).

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

NXProject wasn't born as a product. It was born to solve a real problem.

At the time, my wife was pursuing a Master's degree in Education Management and needed to build a project schedule for a school ramp renovation. The need seemed simple: organize tasks, dependencies, and track the plan visually.

We looked for free tools, but the open-source options we found were outdated, and commercial alternatives required licenses I didn't have at that moment — I was between jobs.

So one weekend, I decided to build a simple alternative to turn tasks into a visual schedule and make it easier to track the project.

The initial goal was just to solve that one problem.

But as I built it, I realized the challenge was much bigger.

After more than 20 years working in technical leadership in data and software engineering, I kept seeing the same conflict in technology projects: technical tools worked great for development and data engineering teams, while management tools delivered schedules and reports — but often at the cost of parallel processes, rework, and lost traceability.

Technical teams needed to keep working in their day-to-day tools.

Managers needed to understand deadlines, capacity, dependencies, and risks.

Usually, someone had to give something up.

That's when the project stopped being just a schedule generator and evolved into NXProject.

Months later, as the idea matured and AI-assisted development tools advanced — after going deeper into environments like Codex and Claude Code — the product evolved quickly. What started as a simple prototype gained new capabilities in planning, visualization, and integration, allowing me to accelerate the vision that had existed from the beginning.

Later, when I integrated with Azure DevOps, I realized the same concept also helped real software and data engineering teams: teams kept working in their established flow — backlog, code, pipelines, automations, and traceability — while leaders and managers finally gained an integrated view of schedule, dependencies, capacity, and impact.

Today NXProject turns Azure DevOps data into a management view of planning and execution, allowing the technical and the managerial to work together — without friction, without parallel processes, and without giving up traceability.

---

## Download

**First time installing?** Download and run the Setup — it installs the .NET runtime, third-party libraries and a Desktop shortcut, then automatically fetches the latest NXProject version:

- [Download NXProject-Setup.zip](../../releases/download/nxsetup-latest/NXProject-Setup.zip)

  The Setup lives in its own fixed release, [`nxsetup-latest`](../../releases/tag/nxsetup-latest), and is republished only when the installer base itself changes — not on every NXProject version.

**Already installed?** Just grab the small update package and extract it over your existing installation:

- [Download NXProject.Community-Release.zip (update)](../../releases/latest/download/NXProject.Community-Release.zip)
- [View release notes and source code downloads](../../releases/latest)

> `NXProject.Community-Release.zip` alone does **not** include the .NET runtime or third-party libraries (PdfSharp, WebView2, CommunityToolkit.Mvvm, LLamaSharp) — it only contains the app files (`.exe`/`.dll`) that change every release, and must be extracted **on top of** an existing installation (created by NXProject-Setup or a previous full release). Use NXProject-Setup.zip for a first-time install on a new machine.
>
> **Third-party licenses**: the distribution includes open-source libraries (mostly under the MIT license). Copyright notices and license texts are in [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt), also shipped inside the zips.
>
> **About Local AI**: besides NXProject's compiled code, the distribution includes `LLamaSharp.dll`, which is **just an open-source .NET wrapper (MIT license)** for llama.cpp — by itself it does not run any model. The native engine (`llama.dll`) and the GGUF model are **not** shipped in the Setup: they are optionally downloaded by NXProject itself (AI menu → Manage Local AI) into a user-chosen folder, with validation and auditable code ([LLamaSharp](https://github.com/SciSharp/LLamaSharp) / [llama.cpp](https://github.com/ggml-org/llama.cpp)).
>
> The binaries were built in an environment with McAfee antivirus. If you prefer to build from source, see the instructions below.

---

## If Windows blocks the .exe

Windows may refuse to run `NXProject.Community.exe` with a "Windows protected your PC" SmartScreen dialog, or simply do nothing when you double-click it. This happens because the binary is unsigned and was downloaded from the internet.

### Option 1 — Unblock via Properties (simplest, no admin required)

1. Right-click `NXProject.Community.exe` → **Properties**
2. At the bottom of the **General** tab, check **Unblock**
3. Click **OK** and double-click the `.exe` again

If the checkbox is not there, the file was already unblocked (or your system uses a stricter policy — see options below).

### Option 2 — Sign with a local developer certificate (recommended for organizations)

Run the script below **as Administrator** once. It creates a self-signed code-signing certificate, installs it as a trusted publisher on the machine, and signs all `.exe`/`.dll` files in the build output:

```powershell
# Run as Administrator in the project root
.\sign-nxproject.ps1
```

After that, run normally with `.\run-community.ps1` or double-click the `.exe`. **No parameter is needed** — the certificate is installed in the machine store and Windows picks it up automatically.

> The certificate is valid for 10 years and covers all future builds as long as you re-run `sign-nxproject.ps1` after each new release.

### Option 3 — WDAC supplemental policy (for corporate environments with strict execution policy)

If your organization enforces Windows Defender Application Control (WDAC) and neither option above works, run the WDAC script as Administrator to allow the NXProject folder:

```powershell
# Run as Administrator
.\allow-nxproject-wdac.ps1
```

This creates a supplemental WDAC policy that allows executables from the NXProject folder. A reboot may be required.

> This option is only needed in tightly locked corporate environments. Most users only need Option 1 or 2.

---

## Screenshots

![TaskBoard, Person & Task view](ScreenShot/Tela06-TaskBoard-Person-Task-EN.svg)
![Azure DevOps Backlog concept](ScreenShot/Tela05-Azure-DevOps-Backlog-EN.svg)

> The two images above are conceptual illustrations with fictional data, not screenshots.

![AI assistant: free-text request](ScreenShot/Tela01.png)
![AI assistant: suggested activities in tabular form](ScreenShot/Tela02.png)
![Schedule grid and Gantt chart](ScreenShot/Tela03.png)
![TFS / Azure DevOps import](ScreenShot/Tela04.png)

> These application screenshots were captured with the Portuguese UI; the product itself is bilingual and switches to English automatically.

---

## Who is NXProject for

| Role | What NXProject delivers |
|---|---|
| **Project Manager** | Schedule integrated with the backlog, delay alerts, dependency view |
| **Scrum Master / RTE** | Capacity per sprint, allocation conflicts, impact of date changes |
| **Tech Lead** | Features and Stories with predecessors and hour-based estimates |
| **PMO** | Multi-project consolidation, export to MS Project / Excel |

---

## Where NXProject plugs in

NXProject works against two servers, and the schedule, the TaskBoard and the Gantt are the same
in both. Each has its own page, with what it reads, what it writes back and how to set it up:

| | |
|---|---|
| [**Azure DevOps**](docs/devops/README.md) | Work items, custom fields, backlog order by `StackRank`, attachments and sync with concurrency control |
| [**GitHub Projects**](docs/gitproject/README.md) | Project (board) fields, hierarchy from sub-issues, backlog order by item position, attachments versioned in the repository |

Each page also links to a companion document with the **state and the decisions** behind that
connector — including what was measured against what the design assumed. That is for whoever
maintains the code; the pages above are for whoever uses it.

## Degrees of freedom: the schedule stops at the Story, the Task lives in the TaskBoard

NXProject plans down to the **Story** level. A Task is **not planned in the schedule** — it is born
and run in the **TaskBoard**, during execution, by whoever is doing the work.

That is a choice, not a limitation. Inspired by the mathematical concept of **degrees of freedom** —
used to model complex systems — NXProject structures complexity without constraining the process:
planning sets the **boundaries** (dates, resources, dependencies, backlog order) and preserves the
space the technical team needs to move autonomously within them.

In practice, NXProject's degree of freedom **is the TaskBoard**:

- managers see Story, deadline, allocation and impact — and that is what they commit to;
- developers create, split, move and close Tasks on the TaskBoard, without asking the schedule for
  permission;
- Task hours and progress roll up to the Story, and the schedule adjusts itself.

Anyone who wants Tasks in the schedule can load them (**Load Task ToDo**, on Azure DevOps), but
that trades away part of this freedom on purpose — and NXProject keeps working either way.

## Usability

- **Interactive Gantt chart** with zoom by day, sprint, or custom period
- **Task dependencies** (predecessors), including across Stories from different Epics
- **TaskBoard** (Project & Story and Person & Task views): drag-to-change state, Doing/Done, WIP limit per person and batched write-back to DevOps
- **Task Plan**: Excel sheet that breaks a Story down into Tasks, applies them to the schedule and syncs back
- **Resource allocation**: workload view per person and period
- **Project Allocation Map**: distribution per person, project and month, with time reporting
- **Cost per resource**: hourly or monthly rate, with totals by Feature, person and month
- **Critical path (CPM)** and **baseline** to compare planned vs. replanned
- **Project Health Check**: lists delayed tasks and tasks with no assignee
- **Configurable calendar**: holidays, working hours per day, weekdays
- **Export**: MS Project XML, OpenProj, Excel XML, CSV, **PDF (landscape)**
- **AI**: assistant for task structure suggestions, with an optional **Local AI** (model running on your own machine, no data sent to the cloud)
- **Project Portfolio**: several DevOps projects in one shared file, with OPEX/CAPEX and an admin group (`Adm_NX`)
- **Tech Lead window**: fetch, create and edit DevOps Tasks per Story; cascade Epic → Feature → Story selection from the toolbar, or open directly from a Story's context menu
- **TKs column** (expanded mode): shows the count of DevOps child Tasks per Story — red when zero, so Stories with no technical tasks are immediately visible
- **Custom DevOps Fields**: configurable classification fields per work item type (Epic, Feature, Story); values are read on import and editable via right-click context menu
- **Double-click to edit** task names, preventing accidental edits when navigating the grid
- **Multilingual**: Portuguese (Brazil) and English, auto-detected from Windows, switchable in Settings

---

## Build from source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [VS Code](https://code.visualstudio.com/download).

```powershell
# Set up environment
.\setup-community-vscode.ps1

# Build
.\build-community.ps1 -Configuration Release

# Or build the release package (update zip + Setup)
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
> A self-contained publish solves this because the runtime is copied next to the `.exe` in the publish folder.
>
> Note: the `NXProject.Community-Release.zip` shipped in the releases is the **update package** — it carries only the files that change in each version and depends on an installation made by `NXProject-Setup.zip`. To install on a fresh machine, always use the Setup.

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
