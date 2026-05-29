# ClaudeTraceHub - Project Walkthrough

A comprehensive guide to the ClaudeTraceHub project: architecture, file structure, data flow, and developer reference.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Tech Stack](#2-tech-stack)
3. [Prerequisites](#3-prerequisites)
4. [Project Structure](#4-project-structure)
5. [Getting Started](#5-getting-started)
6. [Architecture](#6-architecture)
7. [Data Flow](#7-data-flow)
8. [Configuration](#8-configuration)
9. [Models](#9-models)
10. [Services](#10-services)
11. [Pages & Components](#11-pages--components)
12. [Styling & Theming](#12-styling--theming)
13. [Versioning](#13-versioning)
14. [Build & Deployment Scripts](#14-build--deployment-scripts)
15. [File Reference](#15-file-reference)

---

## 1. Overview

**ClaudeTraceHub** is a .NET 9.0 Blazor Server application that provides a web-based UI for browsing Claude Code conversation traces and linking them to Azure DevOps / TFS work items.

### What It Does

- **Discovers** Claude Code conversation data from `~/.claude/projects/` JSONL files
- **Parses** JSONL session files into structured conversations with messages, tool usages, and file changes
- **Displays** a dashboard with activity charts, model usage stats, and recent conversations
- **Browses** projects and individual conversation sessions with a timeline view
- **Shows** GitHub-style diffs for files created/modified during conversations
- **Tracks** Claude Code usage statistics (tokens, models, branches, hourly patterns)
- **Integrates** with Azure DevOps / TFS to discover work items linked to git branches
- **Reports** AI adoption metrics per team member (AI vs manual tasks, hours saved)
- **Tracks** TFS efficiency with day-by-day work item history, comments, and per-task drilldowns
- **Generates** branch names from selected TFS iterations
- **Exports** conversations and reports to Excel format
- **Auto-refreshes** when new conversation data is written by Claude Code
- **Reads** the local Claude account credentials and exposes plan / session info in Settings
- **Supports** multiple color themes with light/dark mode

---

## 2. Tech Stack

| Component          | Technology                     | Version  |
|--------------------|--------------------------------|----------|
| Runtime            | .NET 9.0                       | 9.0.314  |
| Runtime            | .NET 9.0                       | 9.0.314  |
| Web Framework      | Blazor Server (Interactive)    | -        |
| UI Components      | MudBlazor                      | 8.15.0   |
| Excel Export       | ClosedXML                      | 0.105.0  |
| Markdown Rendering | Markdig                        | 0.44.0   |
| API Integration    | Azure DevOps REST API          | v5.0     |

---

## 3. Prerequisites

- **.NET 9 SDK** (9.0.314 or compatible) - [download](https://dot.net/download)
- **.NET 9 SDK** (9.0.314 or compatible) - [download](https://dot.net/download)
- **Claude Code CLI** installed and used (generates `~/.claude/projects/` data)
- **Azure DevOps / TFS** instance (optional, for work item integration)

---

## 4. Project Structure

```
ClaudeTraceHub/
├── ClaudeTraceHub.sln                    # Solution file (single project)
├── Directory.Build.props                 # Centralized version & metadata
├── global.json                           # Pins .NET SDK
├── .gitignore                            # .NET/Blazor template
├── tracehub.bat                          # Main CLI entry point
│
├── scripts/                              # Developer utility scripts
│   ├── build.bat                         #   Build the solution
│   ├── clean.bat                         #   Clean build artifacts
│   ├── restore.bat                       #   Restore NuGet packages
│   ├── run.bat                           #   Run the app locally
│   └── bump-version.bat                  #   Version management tool
│
└── ClaudeTraceHub.Web/                   # The Blazor Server project
    ├── ClaudeTraceHub.Web.csproj         # Project file & dependencies
    ├── Program.cs                        # Entry point & service registration
    ├── appsettings.json                  # Base configuration (incl. NavMenu order)
    ├── appsettings.Development.json      # Dev-only overrides
    ├── usersettings.json                 # User-saved Azure DevOps settings
    │
    ├── Models/                           # Data models (POCOs)
    │   ├── AzureDevOpsSettings.cs        #   TFS connection settings
    │   ├── ClaudeAccountModels.cs        #   Claude account info (plan, session)
    │   ├── ConversationModels.cs         #   Core domain models
    │   ├── DashboardModels.cs            #   Dashboard chart data
    │   ├── JsonlModels.cs                #   JSONL file deserialization
    │   ├── TfsModels.cs                  #   TFS work item models
    │   ├── AiAdoptionModels.cs           #   AI adoption metrics models
    │   ├── TfsEfficiencyModels.cs        #   TFS efficiency tracker models
    │   └── UsageStatisticsModels.cs      #   Usage statistics models
    │
    ├── Services/                         # Business logic layer
    │   ├── ClaudeDataDiscoveryService.cs #   Discovers projects & sessions
    │   ├── JsonlParserService.cs         #   Parses JSONL → Conversation
    │   ├── ConversationCacheService.cs   #   In-memory cache for parsed data
    │   ├── DataRefreshService.cs         #   FileSystemWatcher for live updates
    │   ├── DashboardService.cs           #   Aggregates dashboard statistics
    │   ├── UsageStatisticsService.cs     #   Aggregates usage / token metrics
    │   ├── AzureDevOpsService.cs         #   TFS/Azure DevOps REST client
    │   ├── TfsWorkItemFilterService.cs   #   Branch scanning & WI linking
    │   ├── AiAdoptionService.cs          #   AI vs manual adoption analytics
    │   ├── TfsEfficiencyService.cs       #   Per-member day-wise TFS history
    │   ├── ClaudeAccountService.cs       #   Reads ~/.claude credentials
    │   ├── ExcelExportService.cs         #   Conversation → Excel export
    │   ├── ThemeService.cs               #   Theme & dark mode management
    │   ├── LineDiffHelper.cs             #   LCS-based line diff algorithm
    │   └── SettingsService.cs            #   Persists user settings to JSON
    │
    ├── Components/
    │   ├── App.razor                     # Root HTML document (head/body)
    │   ├── Routes.razor                  # Router configuration
    │   ├── _Imports.razor                # Global using directives
    │   │
    │   ├── Layout/
    │   │   ├── MainLayout.razor          #   App shell (AppBar, Drawer, Content)
    │   │   └── NavMenu.razor             #   Config-driven sidebar nav + version footer
    │   │
    │   └── Pages/
    │       ├── Home.razor                #   Dashboard (route: /)
    │       ├── Projects.razor            #   Project listing (route: /projects)
    │       ├── ProjectDetail.razor       #   Sessions for a project (route: /project/{id})
    │       ├── ConversationViewer.razor  #   Full conversation view (route: /conversation/{proj}/{session})
    │       ├── UsageStatistics.razor     #   Usage stats (route: /usage-stats)
    │       ├── TfsWorkItemExplorer.razor #   TFS work item explorer (route: /tfs-explorer)
    │       ├── AiAdoption.razor          #   AI Adoption Data (route: /ai-adoption)
    │       ├── TfsEfficiency.razor       #   TFS Efficiency Tracker (route: /tfs-efficiency)
    │       ├── BranchCreation.razor      #   Branch Name Generator (route: /branch-creation)
    │       ├── Settings.razor            #   Settings + Claude Account (route: /settings)
    │       ├── FileChangeDialog.razor    #   Dialog: GitHub-style diff viewer
    │       ├── TfsWorkItemsDialog.razor  #   Dialog: Work items for a branch
    │       └── Error.razor               #   Error page
    │
    ├── Properties/
    │   └── launchSettings.json           # VS launch profiles
    │
    └── wwwroot/                          # Static web assets
        ├── app.css                       #   Global styles
        ├── favicon.png                   #   App icon
        └── js/
            └── download.js               #   JS interop for file downloads
```

---

## 5. Getting Started

### Quick Start (From Source)

```bash
# 1. Restore packages
scripts\restore.bat

# 2. Build the solution
scripts\build.bat

# 3. Run the application
scripts\run.bat
# → Opens at http://localhost:5000
```

### Using tracehub.bat (CLI)

```bash
# Show version and usage
tracehub.bat

# Build & publish (self-contained executable)
tracehub.bat publish

# Run the published app
tracehub.bat run

# Add to Windows Startup (auto-start on login)
tracehub.bat autostart

# Check status
tracehub.bat status
```

### First Run

1. Navigate to `http://localhost:5000`
2. You'll see the **Welcome Screen** (setup guard)
3. Click **Go to Settings**
4. Enter your Azure DevOps **Organization URL** and **Personal Access Token**
5. Click **Test URL** to verify connectivity and load projects
6. Select one or more projects and click **Save Settings**
7. You'll be redirected to the Dashboard

> The **Claude Account** tab in Settings is independent of Azure DevOps and reads
> `~/.claude/.credentials.json` to surface your plan, rate-limit tier, and session expiry.

---

## 6. Architecture

### Service Lifetimes

```
┌─────────────────────────────────────────────────────────┐
│                    SINGLETON SERVICES                    │
│  (One instance for the entire application lifetime)      │
│                                                          │
│  ClaudeDataDiscoveryService  - Discovers projects/files  │
│  JsonlParserService          - Parses JSONL files        │
│  ConversationCacheService    - MemoryCache wrapper       │
│  DataRefreshService          - FileSystemWatcher         │
│  SettingsService             - Persists usersettings     │
│  ClaudeAccountService        - Reads Claude credentials  │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                     SCOPED SERVICES                      │
│  (One instance per SignalR circuit / user session)       │
│                                                          │
│  DashboardService            - Dashboard aggregations    │
│  UsageStatisticsService      - Usage / token analytics   │
│  ExcelExportService          - Excel file generation     │
│  ThemeService                - Per-user theme state      │
│  TfsWorkItemFilterService    - Branch scan orchestration │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                    HTTPCLIENT SERVICES                   │
│  (Managed by IHttpClientFactory)                         │
│                                                          │
│  AzureDevOpsService          - REST API client           │
│  AiAdoptionService           - AI adoption REST client   │
│  TfsEfficiencyService        - TFS efficiency client     │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                    HOSTED SERVICE                        │
│  (Background service, starts with the app)              │
│                                                          │
│  DataRefreshService          - Also registered as        │
│                                IHostedService            │
└─────────────────────────────────────────────────────────┘
```

### Render Mode

All pages use `@rendermode InteractiveServer` (Blazor Server). The UI runs on the server with real-time updates via SignalR.

### Middleware Pipeline

```
Request → StaticFiles → Antiforgery → RazorComponents (InteractiveServer)
```

---

## 7. Data Flow

### Conversation Data Pipeline

```
~/.claude/projects/
  └── {project-dir}/
      ├── sessions-index.json     ← Fast metadata (preferred)
      ├── {session-id}.jsonl      ← Full conversation data
      └── ...

         ┌──────────────────────────┐
         │  ClaudeDataDiscoveryService│
         │  • Enumerates project dirs │
         │  • Reads sessions-index    │
         │  • Decodes dir names       │
         │    (d--Projects-foo →      │
         │     D:\Projects\foo)       │
         └──────────┬───────────────┘
                    │
         ┌──────────▼───────────────┐
         │   JsonlParserService      │
         │  • ScanMetadata() → fast  │
         │    (timestamps, count,    │
         │     first prompt, branch) │
         │  • ParseFile() → full     │
         │    (messages, tool usage,  │
         │     file changes, tokens)  │
         └──────────┬───────────────┘
                    │
         ┌──────────▼───────────────┐
         │ ConversationCacheService  │
         │  • MemoryCache (5 min)    │
         │  • Invalidates on file    │
         │    modification           │
         └──────────┬───────────────┘
                    │
         ┌──────────▼───────────────┐
         │    DataRefreshService     │
         │  • FileSystemWatcher on   │
         │    projects directory     │
         │  • Debounced (2 sec)      │
         │  • Fires OnDataChanged    │
         │    event → pages refresh  │
         └──────────────────────────┘
```

### TFS Work Item Discovery

```
Git Branch Name
      │
      ▼
┌─────────────────────────┐
│ Step 1: Pull Requests    │   Search all repos in configured projects
│   Search by sourceRef    │   for PRs with matching source branch.
│   → Get linked work items│   If found → DiscoveryPath.LinkedViaPullRequest
└───────────┬─────────────┘
            │ (no PR matches)
            ▼
┌─────────────────────────┐
│ Step 2: Branch Name      │   Apply regex patterns from settings:
│   Extract work item IDs  │   - feature/12345 → 12345
│   from branch name       │   - bug/6789 → 6789
│   → Fetch by ID          │   If found → DiscoveryPath.ExtractedFromBranchName
└───────────┬─────────────┘
            │ (no IDs extracted)
            ▼
┌─────────────────────────┐
│ Step 3: Not Found        │   → DiscoveryPath.NotFound
└─────────────────────────┘
```

### TFS Efficiency Aggregation

```
Selected: Project + Team + Iteration + Date
            │
            ▼
   Get team area paths      ──► WIQL: tasks in iteration & area
            │
            ▼
   Fetch work item details (AssignedTo, Estimates, Completed, Remaining)
            │
            ├─► Fetch updates per WI (parallel, throttled to 5)  ──► Field deltas
            └─► Fetch comments per WI (parallel, throttled to 5) ──► Comments by date
            │
            ▼
   Aggregate by member → day → details + comments + tasks
```

---

## 8. Configuration

### appsettings.json

```json
{
  "Logging": { ... },
  "AllowedHosts": "*",
  "NavMenu": {
    "Order": [
      "Dashboard",
      "Projects",
      "UsageStatistics",
      "TfsExplorer",
      "TfsEfficiency",
      "AiAdoption",
      "BranchCreation"
    ]
  },
  "AzureDevOps": {
    "ApiVersion": "5.0",
    "BranchWorkItemPatterns": [
      "^(?:feature|bug|hotfix|task|requirement|cr|mgr)/?\\s*(\\d+)",
      "(\\d{4,})"
    ]
  }
}
```

`NavMenu.Order` controls the visible order of sidebar entries. Any key listed in
`NavMenu.razor`'s built-in registry but missing from config is appended at the
end so a misconfigured array never hides a page. The `Settings` link is always
shown beneath a divider.

### usersettings.json (created by Settings page)

```json
{
  "AzureDevOps": {
    "OrganizationUrl": "https://dev.azure.com/yourorg/",
    "PersonalAccessToken": "your-pat-here",
    "Projects": ["ProjectA", "ProjectB"]
  }
}
```

User settings are layered on top of `appsettings.json` with `reloadOnChange: true`, so changes take effect without restarting.

### Configuration Layering

```
appsettings.json                  ← Base defaults (API version, patterns, NavMenu order)
  ↓ overridden by
appsettings.Development.json      ← Dev-only settings (DetailedErrors)
  ↓ overridden by
usersettings.json                 ← User-saved settings (URL, PAT, projects)
  ↓ bound to
IOptionsSnapshot<AzureDevOpsSettings>  ← Per-request snapshot
```

---

## 9. Models

### ConversationModels.cs — Core Domain

| Class                | Purpose                                                    |
|----------------------|------------------------------------------------------------|
| `ClaudeProject`      | A discovered project directory with session count/metadata |
| `SessionSummary`     | Lightweight metadata for a session (no full messages)      |
| `Conversation`       | Full parsed conversation with messages, tokens, file diffs |
| `ConversationMessage`| A single user or assistant message                         |
| `ToolUsageInfo`      | Tool call details (name, file path, action, content)       |
| `FileActionType`     | Enum: None, Created, Modified, Read                        |
| `FileTouchedInfo`    | Summary of a file touched during conversation              |
| `FileChangeTimeline` | Ordered list of changes to a specific file                 |
| `FileChangeEntry`    | One change step (with old/new content, timestamps)         |
| `DiffLine`           | A single line in a unified diff                            |
| `DiffHunk`           | A group of diff lines with context                         |

### JsonlModels.cs — JSONL Deserialization

Maps to Claude Code's JSONL format:

| Class               | Maps To                        |
|---------------------|--------------------------------|
| `JsonlEntry`        | One line in a `.jsonl` file    |
| `JsonlMessage`      | `message` field (role, model)  |
| `JsonlUsage`        | `usage` field (input/output tokens) |
| `JsonlContentBlock` | Content array elements (text, tool_use, thinking) |
| `SessionsIndex`     | `sessions-index.json` file     |
| `SessionIndexEntry` | One entry in the sessions index|

### TfsModels.cs — Work Item Integration

| Class                    | Purpose                                     |
|--------------------------|---------------------------------------------|
| `TfsWorkItem`            | A TFS/Azure DevOps work item                |
| `TfsQueryResult`         | Result of work item discovery for a branch  |
| `BranchSessionGroup`     | Sessions grouped by git branch              |
| `WorkItemConversationLink`| Links a work item to its conversation sessions |
| `WorkItemScanResult`     | Full scan result (linked + unlinked)        |
| `ScanProgress`           | Progress reporting during branch scan       |
| `DiscoveryPath`          | Enum: how a work item was discovered        |

### DashboardModels.cs — Dashboard Statistics

| Class                | Purpose                         |
|----------------------|---------------------------------|
| `DashboardStats`     | Summary card values             |
| `ConversationsPerDay`| Line chart data point           |
| `MessagesPerProject` | Bar chart data point            |
| `TokenUsagePoint`    | Token usage over time           |
| `ModelDistribution`  | Donut chart data point          |

### UsageStatisticsModels.cs — Usage Analytics

| Class                  | Purpose                                                |
|------------------------|--------------------------------------------------------|
| `UsageDataBundle`      | Top-level bundle returned by `UsageStatisticsService`  |
| `UsageSummary`         | Total tokens, messages, sessions, tools                |
| `BranchUsageStats`     | Per-branch token / session aggregates                  |
| `ModelUsageStats`      | Per-model token / message aggregates                   |
| `ProjectUsageStats`    | Per-project aggregates                                 |
| `DailyUsagePoint`      | Daily totals for the time-series chart                 |
| `DailyModelUsagePoint` | Daily per-model split (stacked chart)                  |
| `HourlyUsagePoint`     | 24-hour token distribution                             |
| `ToolUsageStats`       | Tool invocation counts and unique-session counts       |

### AiAdoptionModels.cs — AI Adoption Metrics

| Class                  | Purpose                                                            |
|------------------------|--------------------------------------------------------------------|
| `AdoptionDataBundle`   | Top-level bundle returned by `AiAdoptionService`                   |
| `MemberAdoptionStats`  | Per-member AI vs manual task counts, hours saved, discipline       |
| `RawWorkItem`          | Flat work item record used to power per-member task drilldowns     |
| `AdoptionFilter`       | Filter criteria (project, team, iteration, dates, discipline)      |

### TfsEfficiencyModels.cs — Daily Efficiency Tracker

| Class                    | Purpose                                                       |
|--------------------------|---------------------------------------------------------------|
| `EfficiencyTrackerBundle`| Top-level bundle returned by `TfsEfficiencyService`           |
| `MemberDailyEfficiency`  | Per-member roll-up: totals, day history, expandable task list |
| `DayWiseBreakdown`       | One day's deltas, work-item updates, and `CommentsByWorkItem` |
| `WorkItemFieldDelta`     | A single field-level change (Completed/Remaining)             |
| `WorkItemComment`        | A single comment on a work item (text, author, timestamp)     |
| `TaskSummary`            | Snapshot of a task shown when a member row is expanded        |

### ClaudeAccountModels.cs — Claude Credentials

| Class               | Purpose                                                                |
|---------------------|------------------------------------------------------------------------|
| `ClaudeAccountInfo` | Login state, auth method, email, plan, rate-limit tier, token expiry   |

### AzureDevOpsSettings.cs — Configuration Model

Bound from `appsettings.json` + `usersettings.json`. Contains `OrganizationUrl`, `PersonalAccessToken`, `Projects`, `ApiVersion`, `BranchWorkItemPatterns`, and a computed `IsConfigured` property.

---

## 10. Services

### ClaudeDataDiscoveryService (Singleton)

**File:** `Services/ClaudeDataDiscoveryService.cs`

Discovers Claude Code projects from `~/.claude/projects/`. Decodes directory names (e.g., `d--Projects-foo` → `D:\Projects\foo`). Reads `sessions-index.json` for fast metadata; falls back to scanning individual JSONL files.

**Key Methods:**
- `GetAllProjects()` → `List<ClaudeProject>`
- `GetSessionsForProject(projectDirName)` → `List<SessionSummary>`
- `GetAllSessions()` → all sessions across all projects
- `GetJsonlFilePath(projectDirName, sessionId)` → file path or null

---

### JsonlParserService (Singleton)

**File:** `Services/JsonlParserService.cs`

Parses Claude Code JSONL files. Has two modes:
- **`ScanMetadata()`** — Fast scan: extracts timestamps, message count, first prompt, git branch without parsing full content
- **`ParseFile()`** — Full parse: builds complete `Conversation` with messages, tool usages, file changes, and token counts

Handles Claude Code's message format: groups assistant entries by message ID, extracts text/tool_use/thinking blocks, strips IDE tags and system reminders, tracks Write/Edit/Read tool calls for file change tracking.

---

### ConversationCacheService (Singleton)

**File:** `Services/ConversationCacheService.cs`

Wraps `IMemoryCache` with file-modification-aware caching. Parsed conversations are cached for 5 minutes (sliding expiration). Re-parses when the JSONL file's last-write timestamp changes.

---

### DataRefreshService (Singleton + HostedService)

**File:** `Services/DataRefreshService.cs`

Uses `FileSystemWatcher` on the Claude projects directory. Monitors `.jsonl` and `sessions-index.json` files. Debounces notifications (2 seconds after last change). Fires `OnDataChanged` event that pages subscribe to for live refresh.

---

### DashboardService (Scoped)

**File:** `Services/DashboardService.cs`

Aggregates data for the dashboard page:
- **Stats:** total conversations, messages, active projects (30 days)
- **Charts:** conversations per day (30 days), messages per project (top 10), model distribution (samples 50 recent sessions)
- **Recent:** latest 15-20 conversations

---

### UsageStatisticsService (Scoped)

**File:** `Services/UsageStatisticsService.cs`

Aggregates Claude Code usage from cached/parsed sessions over a date range:
token totals (input/output/total), per-branch / per-model / per-project rollups,
daily and 24-hour time series, tool invocation counts. Powers the
**Usage Statistics** page.

---

### AzureDevOpsService (HttpClient)

**File:** `Services/AzureDevOpsService.cs`

REST client for Azure DevOps / TFS. Uses Basic auth with PAT. Implements the three-step work item discovery: PR-linked → branch name extraction → not found. Also handles connection verification and project listing for the Settings page.

**Key Methods:**
- `GetWorkItemsForBranchAsync(branchName)` → `TfsQueryResult`
- `VerifyAndFetchProjectsAsync(url, pat)` → connection test + project list
- `ExtractWorkItemIds(branchName)` → regex-based ID extraction

---

### TfsWorkItemFilterService (Scoped)

**File:** `Services/TfsWorkItemFilterService.cs`

Orchestrates the TFS Explorer page's "Scan All Branches" feature. Groups all sessions by git branch, queries Azure DevOps for each branch, merges results into `WorkItemConversationLink` objects, and reports scan progress.

---

### AiAdoptionService (HttpClient)

**File:** `Services/AiAdoptionService.cs`

REST client that drives the **AI Adoption Data** page. Discovers the
TFS/Azure-DevOps custom fields used to flag AI vs manual tasks (cached
process-wide), queries tasks for the chosen project / team / iteration, and
rolls them up into `MemberAdoptionStats` plus a flat `RawWorkItem` list used by
the per-member task drilldown.

---

### TfsEfficiencyService (HttpClient)

**File:** `Services/TfsEfficiencyService.cs`

REST client that drives the **TFS Efficiency Tracker**. For a chosen
project / team / iteration / date it:

1. Resolves the team's area paths.
2. Runs WIQL to find tasks in the iteration scoped to those area paths.
3. Fetches current work item details (Assigned To, Original Estimate, Completed, Remaining).
4. Fetches per-WI updates (field deltas) and comments in parallel, throttled to 5 concurrent requests.
5. Aggregates by member → day, attaching `CommentsByWorkItem` and a per-member `Tasks` snapshot for drilldowns.

---

### ClaudeAccountService (Singleton)

**File:** `Services/ClaudeAccountService.cs`

Reads `~/.claude/.credentials.json` for the local Claude Code session, then
optionally enriches it with the Anthropic profile API to surface email, display
name, organization name, plan, and rate-limit tier. Results are cached for
5 minutes (1 minute on network failure). Used by the **Claude Account** tab on
the Settings page.

---

### ExcelExportService (Scoped)

**File:** `Services/ExcelExportService.cs`

Generates Excel workbooks using ClosedXML with two sheets:
- **Summary:** session ID, project, dates, branch, message count, token count
- **Messages:** numbered rows with timestamp, role, message text, model, tokens, tools used

Color-coded rows (green for user, blue for assistant) with styled headers.

---

### ThemeService (Scoped)

**File:** `Services/ThemeService.cs`

Manages 4 color themes (Purple, Ocean Blue, Forest Green, Sunset) with full light/dark palettes. Theme preference is persisted in `localStorage` via JS interop and restored on page load.

---

### LineDiffHelper (Static)

**File:** `Services/LineDiffHelper.cs`

LCS-based (Longest Common Subsequence) line diff algorithm. Computes unified diffs between old/new text. Groups diff lines into hunks with configurable context lines (default 3). Falls back to simple remove-all/add-all for large files (> 100K line products).

---

### SettingsService (Singleton)

**File:** `Services/SettingsService.cs`

Writes Azure DevOps settings to `usersettings.json`. Works with the configuration system's `reloadOnChange: true` for live updates.

---

## 11. Pages & Components

### Layout

**MainLayout.razor** — The app shell containing:
- **AppBar:** Title, theme color picker, dark mode toggle
- **Drawer:** Sidebar with `NavMenu` component
- **Content area:** Setup guard (redirects to Settings if not configured) or page body

**NavMenu.razor** — Config-driven sidebar navigation:
- Reads the visible order from `NavMenu:Order` in `appsettings.json`
- Built-in entries: Dashboard, Projects, Usage Statistics, TFS Explorer,
  AI Adoption, TFS Efficiency, Branch Creation
- Settings link always rendered below a divider
- Pages requiring Azure DevOps configuration are disabled until setup is complete
- Footer shows version (from `AssemblyInformationalVersion`, build-metadata
  suffix stripped) and copyright

### Pages

#### Dashboard (`/`)
**File:** `Home.razor`

Four stat cards (Total Conversations, Total Messages, Active Projects, Avg Msgs/Conversation), Conversations Per Day line chart (30 days) with summary, Model Usage donut chart, Top Projects horizontal bar chart, Recent Conversations clickable table.

#### Projects (`/projects`)
**File:** `Projects.razor`

Searchable/sortable table of all discovered Claude Code projects. Columns: Project Name, Path, Sessions, Messages, Last Activity. Click a row to navigate to project detail.

#### Project Detail (`/project/{ProjectDirName}`)
**File:** `ProjectDetail.razor`

Lists all conversation sessions for a specific project. Filterable by first prompt, git branch, or session ID. Click a row to view the full conversation.

#### Conversation Viewer (`/conversation/{ProjectDirName}/{SessionId}`)
**File:** `ConversationViewer.razor`

Full conversation display with:
- **Header:** Project, date, branch (clickable for TFS lookup), message count
- **Files Touched:** Expandable panel grouped by Created/Modified/Read. Click any file to open the diff dialog.
- **Message Thread:** Chat-style layout — user ("You") messages aligned on the right with the avatar on the right, assistant ("Claude") messages aligned on the left with the avatar on the left. Vertical timeline, Markdown-rendered message content, expandable tool usage details, metadata (timestamp, model, tokens)
- **Export to Excel** button

#### Usage Statistics (`/usage-stats`)
**File:** `UsageStatistics.razor`

Token / activity dashboard for Claude Code usage:
- Date-range presets (7d / 14d / 30d / 90d) plus a date-range picker
- Summary cards: total tokens, messages, sessions, tools used
- Time-series chart with optional per-model stack
- Hourly usage chart (24 buckets)
- Per-branch, per-project, and per-model tables
- Subscribes to `DataRefreshService.OnDataChanged` for live refresh

#### TFS Work Item Explorer (`/tfs-explorer`)
**File:** `TfsWorkItemExplorer.razor`

Filter panel with: Work Item ID, Type, Status, Assigned To, Branch Name, Chat History Link status. "Scan All Branches" button triggers async scanning with progress bar. Results show work item cards with linked conversation sessions, and a separate section for unlinked sessions.

#### AI Adoption Data (`/ai-adoption`)
**File:** `AiAdoption.razor`

Per-member breakdown of AI vs manual task adoption:
- Filters: project, team, iteration, discipline, date range
- Sortable member table (Tasks, Claude AI count, Manual count, Hours Saved, etc.)
- **Expandable per-member task drilldown** — clicking a member name reveals
  a nested table of every task they own with Type (AI/Manual), Original Estimate,
  Completed, and Remaining hours, plus totals
- Excel export of the full report

#### TFS Efficiency Tracker (`/tfs-efficiency`)
**File:** `TfsEfficiency.razor`

Daily TFS hours-tracking dashboard scoped to a project / team / iteration / date:
- Member rows show total Completed and Remaining for the iteration
- Expanding a member reveals:
  - **Task summary table** (per-task Original Estimate / Completed / Remaining with totals)
  - **Day history** with field-level deltas, work item titles, and inline
    work-item comments (collapsible per WI, formatted into header + bullets)
- Subscribed to Azure DevOps comments API (`api-version=7.1-preview.3`)

#### Branch Creation (`/branch-creation`)
**File:** `BranchCreation.razor`

Helper that turns a TFS iteration + description into a normalized git branch
name. Auto-detects iteration from the chosen project / team when configured;
copy-to-clipboard for the generated name.

#### Settings (`/settings`)
**File:** `Settings.razor`

Tabbed settings page:
- **Claude Account** tab — auth method, email/name, organization, plan,
  rate-limit tier, session expiry, and a colored active/expired indicator
  (data sourced from `ClaudeAccountService`)
- **Azure DevOps** tab — first-run wizard with checklist, Organization URL
  input, PAT input (with show/hide toggle), Test URL button (verifies
  connection and loads projects), multi-select project picker, Save button.
  Context-aware error messages for auth failures, network issues, SSL problems.

### Dialogs

#### FileChangeDialog
Shows a GitHub-style diff for a specific file's changes during a conversation. Step-by-step operations with +/- line counts, stat bars, hunk headers, expandable large content.

#### TfsWorkItemsDialog
Shows work items discovered for a git branch. Grouped by type (Requirements, Change Requests, Bugs, Other) with state badges, assigned-to info, and a discovery log.

---

## 12. Styling & Theming

### CSS Architecture

**File:** `wwwroot/app.css`

Organized into numbered sections, including (non-exhaustive):
1. Background
2. Stats Cards — Hover animations, colored left borders
3. Message Bubbles & Thread Line — Vertical timeline with avatars
4. Tables — Striped rows, hover effects, sortable headers
5. Charts — Custom legends, horizontal bar chart, summary bars
7. Nav Drawer — Brand header, active link styling, footer
8. Loading States — Centered spinner with fade-in
9. Conversation Header — Gradient top border
10. Files Touched Panel — Color-coded file groups
11. File Change Viewer / Diff Display — Full GitHub-style diff
11b. TFS Work Items Dialog — Branch info, work item cards
12. Animations — fadeIn, fadeSlideUp
13. TFS Work Item Explorer — Filter panel, scan progress, cards
14. Markdown Rendered Content — Full GFM styling
15. Setup Guard / First-Run Screen

### Theme System

- CSS class on `<html>`: `light-theme` or `dark-theme`
- Applied immediately from `localStorage` (before Blazor loads) to prevent flash
- MudBlazor `MudThemeProvider` for component theming
- 4 built-in themes with distinct light/dark palettes

### Reconnect UI

Custom-styled reconnect modal (replaces Blazor's default). Dark overlay with blur, styled button, network offline/online detection via JS.

---

## 13. Versioning

### Version Source of Truth

**File:** `Directory.Build.props`

```xml
<Version>1.9.0</Version>
```

This single `<Version>` property drives `AssemblyVersion`, `FileVersion`, and `InformationalVersion` automatically via the .NET SDK.

### Version Display

- **UI:** NavMenu sidebar footer reads version from `AssemblyInformationalVersionAttribute`
- **CLI:** `tracehub.bat version` reads from `Directory.Build.props` using XML parsing

### bump-version.bat

**File:** `scripts/bump-version.bat`

Full version manager with commands:

| Command           | Description                                    |
|-------------------|------------------------------------------------|
| `auto`            | Auto-detect bump type from git commit messages |
| `major`           | Bump major version (breaking changes)          |
| `minor`           | Bump minor version (new features)              |
| `patch`           | Bump patch version (bug fixes)                 |
| `set x.y.z`       | Set explicit version                           |
| `current`         | Show current version                           |
| `tag`             | Create git tag `v{version}` on current commit  |

**Auto-detection** uses conventional commit prefixes:
- `breaking:`, `major:`, `BREAKING CHANGE` → **major** bump
- `feat:`, `feature:`, `add:`, `update:`, `enhance:` → **minor** bump
- `fix:`, `bugfix:`, `hotfix:`, `patch:`, `perf:` → **patch** bump
- Unrecognized prefixes → **patch** bump (default)

The auto command analyzes commits since the last `v*` tag using PowerShell for reliable parsing.

---

## 14. Build & Deployment Scripts

### scripts/build.bat
Builds the solution. Sets `MSBuildSDKsPath` to `C:\Program Files\dotnet\sdk\9.0.314\Sdks` to ensure the .NET 9 SDK is used.

### scripts/clean.bat
Cleans build artifacts (`dotnet clean`).

### scripts/restore.bat
Restores NuGet packages.

### scripts/run.bat
Runs the app with `dotnet run` on `http://localhost:5000` and `https://localhost:5001`.

### tracehub.bat (Root)
Main CLI entry point. Commands:
- `publish` — Builds a self-contained `win-x64` executable to `publish/` folder
- `run` — Starts the published or built executable
- `autostart` — Adds to Windows Startup folder (no admin required)
- `remove` — Removes from Windows Startup
- `status` — Checks if registered and running
- `version` — Shows current version

> The publish command auto-detects the .NET 9 SDK path and copies `tracehub.bat` into the publish folder, making the published output fully portable.

---

## 15. File Reference

### Solution Root

| File                    | Description                                     |
|-------------------------|-------------------------------------------------|
| `ClaudeTraceHub.sln`    | VS solution file (single project)               |
| `Directory.Build.props` | Centralized version (1.9.0), author, description|
| `global.json`           | Pins SDK version                                |
| `.gitignore`            | .NET/Blazor template + project-specific ignores |
| `tracehub.bat`          | Main CLI (publish, run, autostart, status, version)|

### Scripts

| File                       | Description                              |
|----------------------------|------------------------------------------|
| `scripts/build.bat`        | `dotnet build ClaudeTraceHub.sln`        |
| `scripts/clean.bat`        | `dotnet clean ClaudeTraceHub.sln`        |
| `scripts/restore.bat`      | `dotnet restore ClaudeTraceHub.sln`      |
| `scripts/run.bat`          | `dotnet run --project ClaudeTraceHub.Web`|
| `scripts/bump-version.bat` | SemVer manager (auto/major/minor/patch)  |

### Project Configuration

| File                              | Description                          |
|-----------------------------------|--------------------------------------|
| `ClaudeTraceHub.Web.csproj`       | Target net9.0, MudBlazor + ClosedXML + Markdig |
| `appsettings.json`                | Logging, NavMenu order, API version, branch patterns|
| `appsettings.Development.json`    | DetailedErrors for dev mode          |
| `usersettings.json`               | User-saved Azure DevOps connection   |
| `Properties/launchSettings.json`  | VS launch profiles                   |

### Models

| File                          | Key Types                                              |
|-------------------------------|--------------------------------------------------------|
| `AzureDevOpsSettings.cs`      | `AzureDevOpsSettings` (config binding)                 |
| `ClaudeAccountModels.cs`      | `ClaudeAccountInfo`                                    |
| `ConversationModels.cs`       | `Conversation`, `ConversationMessage`, `ToolUsageInfo`, `FileChangeTimeline`, `DiffLine`, `DiffHunk` |
| `DashboardModels.cs`          | `DashboardStats`, `ConversationsPerDay`, `ModelDistribution` |
| `JsonlModels.cs`              | `JsonlEntry`, `JsonlMessage`, `JsonlContentBlock`, `SessionsIndex` |
| `TfsModels.cs`                | `TfsWorkItem`, `TfsQueryResult`, `WorkItemScanResult`, `ScanProgress` |
| `AiAdoptionModels.cs`         | `AdoptionDataBundle`, `MemberAdoptionStats`, `RawWorkItem`, `AdoptionFilter` |
| `TfsEfficiencyModels.cs`      | `EfficiencyTrackerBundle`, `MemberDailyEfficiency`, `DayWiseBreakdown`, `WorkItemFieldDelta`, `WorkItemComment`, `TaskSummary` |
| `UsageStatisticsModels.cs`    | `UsageDataBundle`, `UsageSummary`, `BranchUsageStats`, `ModelUsageStats`, `ProjectUsageStats`, `DailyUsagePoint`, `HourlyUsagePoint`, `ToolUsageStats` |

### Services

| File                            | Lifetime    | Purpose                              |
|---------------------------------|-------------|--------------------------------------|
| `ClaudeDataDiscoveryService.cs` | Singleton   | Discovers projects & sessions        |
| `JsonlParserService.cs`         | Singleton   | Parses JSONL files                   |
| `ConversationCacheService.cs`   | Singleton   | Memory cache with invalidation       |
| `DataRefreshService.cs`         | Singleton+Hosted | FileSystemWatcher for live updates |
| `SettingsService.cs`            | Singleton   | Writes usersettings.json             |
| `ClaudeAccountService.cs`       | Singleton   | Reads Claude credentials + profile   |
| `DashboardService.cs`           | Scoped      | Dashboard data aggregation           |
| `UsageStatisticsService.cs`     | Scoped      | Usage / token analytics              |
| `ExcelExportService.cs`         | Scoped      | Excel workbook generation            |
| `ThemeService.cs`               | Scoped      | Theme + dark mode state              |
| `TfsWorkItemFilterService.cs`   | Scoped      | Branch scan orchestration            |
| `AzureDevOpsService.cs`         | HttpClient  | Azure DevOps REST API                |
| `AiAdoptionService.cs`          | HttpClient  | AI adoption analytics REST client    |
| `TfsEfficiencyService.cs`       | HttpClient  | TFS efficiency tracker REST client   |
| `LineDiffHelper.cs`             | Static      | LCS diff algorithm                   |

### Components

| File                          | Route                                | Purpose                    |
|-------------------------------|--------------------------------------|----------------------------|
| `App.razor`                   | -                                    | Root HTML document         |
| `Routes.razor`                | -                                    | Router config              |
| `_Imports.razor`              | -                                    | Global usings              |
| `MainLayout.razor`            | -                                    | App shell                  |
| `NavMenu.razor`               | -                                    | Config-driven sidebar nav  |
| `Home.razor`                  | `/`                                  | Dashboard                  |
| `Projects.razor`              | `/projects`                          | Project listing            |
| `ProjectDetail.razor`         | `/project/{ProjectDirName}`          | Project sessions           |
| `ConversationViewer.razor`    | `/conversation/{proj}/{session}`     | Full conversation          |
| `UsageStatistics.razor`       | `/usage-stats`                       | Usage statistics dashboard |
| `TfsWorkItemExplorer.razor`   | `/tfs-explorer`                      | TFS work item explorer     |
| `AiAdoption.razor`            | `/ai-adoption`                       | AI adoption data           |
| `TfsEfficiency.razor`         | `/tfs-efficiency`                    | TFS efficiency tracker     |
| `BranchCreation.razor`        | `/branch-creation`                   | Branch name generator      |
| `Settings.razor`              | `/settings`                          | Claude account + Azure DevOps |
| `FileChangeDialog.razor`      | - (dialog)                           | GitHub-style diff viewer   |
| `TfsWorkItemsDialog.razor`    | - (dialog)                           | Branch work items          |
| `Error.razor`                 | `/Error`                             | Error page                 |

### Static Assets

| File                     | Description                                      |
|--------------------------|--------------------------------------------------|
| `wwwroot/app.css`        | Global styles                                    |
| `wwwroot/js/download.js` | JS interop: creates blob download from byte array |
| `wwwroot/favicon.png`    | Application icon                                 |

---

## Working Flow Summary

```
                    ┌──────────────┐
                    │  Claude Code  │
                    │  CLI Sessions │
                    └──────┬───────┘
                           │ writes JSONL
                           ▼
                    ~/.claude/projects/
                           │
              ┌────────────┼────────────┐
              │            │            │
              ▼            ▼            ▼
         Project A    Project B    Project C
         ├── sessions-index.json
         ├── abc123.jsonl
         └── def456.jsonl
              │
              │ FileSystemWatcher (DataRefreshService)
              ▼
    ┌─────────────────────┐
    │  ClaudeTraceHub.Web  │
    │                      │
    │  Discovery → Parse   │──────────► Dashboard
    │  → Cache → Display   │──────────► Project Browser
    │                      │──────────► Conversation Viewer
    │                      │──────────► Usage Statistics
    │  Azure DevOps API ◄──│──────────► TFS Explorer
    │                      │──────────► AI Adoption Data
    │                      │──────────► TFS Efficiency Tracker
    │                      │──────────► Branch Creation
    │  ~/.claude creds  ◄──│──────────► Settings (Claude Account)
    └──────────┬───────────┘
               │
               ▼
    http://localhost:5000
    (Blazor Server + SignalR)
```
