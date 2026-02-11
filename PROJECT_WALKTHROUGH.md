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
- **Integrates** with Azure DevOps / TFS to discover work items linked to git branches
- **Exports** conversations to Excel format
- **Auto-refreshes** when new conversation data is written by Claude Code
- **Supports** multiple color themes with light/dark mode

---

## 2. Tech Stack

| Component          | Technology                     | Version  |
|--------------------|--------------------------------|----------|
| Runtime            | .NET 9.0                       | 9.0.308  |
| Web Framework      | Blazor Server (Interactive)    | -        |
| UI Components      | MudBlazor                      | 8.15.0   |
| Excel Export       | ClosedXML                      | 0.105.0  |
| Markdown Rendering | Markdig                        | 0.44.0   |
| API Integration    | Azure DevOps REST API          | v5.0     |

---

## 3. Prerequisites

- **.NET 9 SDK** (9.0.308 or compatible) - [download](https://dot.net/download)
- **Claude Code CLI** installed and used (generates `~/.claude/projects/` data)
- **Azure DevOps / TFS** instance (optional, for work item integration)

---

## 4. Project Structure

```
ClaudeTraceHub/
├── ClaudeTraceHub.sln                    # Solution file (single project)
├── Directory.Build.props                 # Centralized version & metadata
├── global.json                           # Pins .NET SDK to 9.0.308
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
    ├── appsettings.json                  # Base configuration
    ├── appsettings.Development.json      # Dev-only overrides
    ├── usersettings.json                 # User-saved Azure DevOps settings
    │
    ├── Models/                           # Data models (POCOs)
    │   ├── AzureDevOpsSettings.cs        #   TFS connection settings
    │   ├── ConversationModels.cs         #   Core domain models
    │   ├── DailySummaryModels.cs         #   Daily summary analytics models
    │   ├── DashboardModels.cs            #   Dashboard chart data
    │   ├── JsonlModels.cs               #   JSONL file deserialization
    │   └── TfsModels.cs                 #   TFS work item models
    │
    ├── Services/                         # Business logic layer
    │   ├── ClaudeDataDiscoveryService.cs #   Discovers projects & sessions
    │   ├── JsonlParserService.cs         #   Parses JSONL → Conversation
    │   ├── ConversationCacheService.cs   #   In-memory cache for parsed data
    │   ├── DailySummaryService.cs        #   Daily analytics & summary generation
    │   ├── DashboardService.cs           #   Aggregates dashboard statistics
    │   ├── DataRefreshService.cs         #   FileSystemWatcher for live updates
    │   ├── AzureDevOpsService.cs         #   TFS/Azure DevOps REST client
    │   ├── TfsWorkItemFilterService.cs   #   Branch scanning & WI linking
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
    │   │   └── NavMenu.razor             #   Sidebar navigation + version footer
    │   │
    │   └── Pages/
    │       ├── Home.razor                #   Dashboard (route: /)
    │       ├── Projects.razor            #   Project listing (route: /projects)
    │       ├── ProjectDetail.razor       #   Sessions for a project (route: /project/{id})
    │       ├── ConversationViewer.razor   #   Full conversation view (route: /conversation/{proj}/{session})
    │       ├── DailySummary.razor         #   Daily summary analytics (route: /daily-summary)
    │       ├── TfsWorkItemExplorer.razor  #   TFS work item explorer (route: /tfs-explorer)
    │       ├── Settings.razor            #   Azure DevOps settings (route: /settings)
    │       ├── FileChangeDialog.razor    #   Dialog: GitHub-style diff viewer
    │       ├── TfsWorkItemsDialog.razor  #   Dialog: Work items for a branch
    │       └── Error.razor               #   Error page
    │
    ├── Properties/
    │   └── launchSettings.json           # VS launch profiles
    │
    └── wwwroot/                          # Static web assets
        ├── app.css                       #   Global styles (~1640 lines)
        ├── favicon.png                   #   App icon
        └── js/
            └── download.js              #   JS interop for file downloads
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

---

## 6. Architecture

### Service Lifetimes

```
┌─────────────────────────────────────────────────────────┐
│                    SINGLETON SERVICES                     │
│  (One instance for the entire application lifetime)      │
│                                                          │
│  ClaudeDataDiscoveryService  - Discovers projects/files  │
│  JsonlParserService          - Parses JSONL files        │
│  ConversationCacheService    - MemoryCache wrapper       │
│  DataRefreshService          - FileSystemWatcher         │
│  SettingsService             - Persists usersettings     │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                     SCOPED SERVICES                      │
│  (One instance per SignalR circuit / user session)       │
│                                                          │
│  DashboardService            - Dashboard aggregations    │
│  DailySummaryService         - Daily analytics & summary │
│  ExcelExportService          - Excel file generation     │
│  ThemeService                - Per-user theme state      │
│  TfsWorkItemFilterService    - Branch scan orchestration │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                    HTTPCLIENT SERVICE                     │
│  (Managed by IHttpClientFactory)                         │
│                                                          │
│  AzureDevOpsService          - REST API client           │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                    HOSTED SERVICE                         │
│  (Background service, starts with the app)               │
│                                                          │
│  DataRefreshService          - Also registered as        │
│                                IHostedService             │
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

---

## 8. Configuration

### appsettings.json

```json
{
  "Logging": { ... },
  "AllowedHosts": "*",
  "AzureDevOps": {
    "ApiVersion": "5.0",
    "BranchWorkItemPatterns": [
      "^(?:feature|bug|hotfix|task|requirement|cr|mgr)/?\\s*(\\d+)",
      "(\\d{4,})"
    ]
  }
}
```

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
appsettings.json                  ← Base defaults (API version, patterns)
  ↓ overridden by
appsettings.Development.json      ← Dev-only settings (DetailedErrors)
  ↓ overridden by
usersettings.json                 ← User-saved settings (URL, PAT, projects)
  ↓ bound to
IOptionsMonitor<AzureDevOpsSettings>  ← Live-reloading options
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

### DailySummaryModels.cs — Daily Analytics

| Class                   | Purpose                                              |
|-------------------------|------------------------------------------------------|
| `DailyStats`            | Aggregated stats for a specific date                 |
| `DailyConversationDetail`| Expanded per-conversation info (tokens, tools, files)|
| `DailyFileActivity`     | File operations aggregated across conversations      |
| `HourlyTokenUsage`      | Hourly token breakdown (0–23) for bar chart          |
| `DailyActivityPoint`    | Per-day activity point for the 30-day mini chart     |

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

### DailySummaryService (Scoped)

**File:** `Services/DailySummaryService.cs`

Provides daily analytics and summary generation. Uses lightweight `SessionSummary` data for fast aggregation, with on-demand full parsing for conversation detail expansion.

**Key Methods:**
- `GetSessionsForDate(date, projectDirName?)` → sessions for a specific date, optionally filtered by project
- `GetDailyStats(date, projectDirName?)` → aggregate stats (conversation/message/project counts)
- `GetActivityOverDays(days)` → 30-day activity points for the mini bar chart
- `GetActiveProjectsForDate(date)` → distinct projects active on a given date
- `GetConversationDetail(session)` → full conversation detail (tokens, tools, files touched)
- `GetDailyFileActivity(date, projectDirName?)` → file operations aggregated across conversations
- `GetHourlyTokenUsage(date, projectDirName?)` → 24-hour token breakdown for the hourly chart
- `GenerateDaySummary(date, sessions, stats)` → human-readable day summary text

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

**NavMenu.razor** — Sidebar navigation with:
- Dashboard, Projects, Daily Summary, TFS Explorer, Settings links
- Version and copyright footer (read from assembly metadata)

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
- **Message Thread:** Vertical timeline with user/assistant avatars, Markdown-rendered message content, expandable tool usage details, metadata (timestamp, model, tokens)
- **Export to Excel** button

#### Daily Summary (`/daily-summary`, `/daily-summary/{DateParam}`)
**File:** `DailySummary.razor`

Date-navigable daily analytics page with:
- **Date navigation:** Previous/Next day buttons, date picker, Today shortcut
- **Project filter:** Dropdown to scope all data to a single project
- **30-day activity bar chart:** Conversation and message counts per day
- **Daily stats cards:** Conversations, Messages, Active Projects for the selected date
- **Hourly token usage chart:** 24-hour input/output token breakdown
- **Conversation cards:** Expandable cards with detail (tokens, tools used, files touched, duration)
- **Files changed list:** Aggregated file operations across all conversations for the day
- **Day summary text:** Human-readable narrative summary of the day's activity

#### TFS Work Item Explorer (`/tfs-explorer`)
**File:** `TfsWorkItemExplorer.razor`

Filter panel with: Work Item ID, Type, Status, Assigned To, Branch Name, Chat History Link status. "Scan All Branches" button triggers async scanning with progress bar. Results show work item cards with linked conversation sessions, and a separate section for unlinked sessions.

#### Settings (`/settings`)
**File:** `Settings.razor`

First-run wizard with checklist. Organization URL input, PAT input (with show/hide toggle), Test URL button (verifies connection and loads projects), multi-select project picker, Save button. Context-aware error messages for auth failures, network issues, SSL problems.

### Dialogs

#### FileChangeDialog
Shows a GitHub-style diff for a specific file's changes during a conversation. Step-by-step operations with +/- line counts, stat bars, hunk headers, expandable large content.

#### TfsWorkItemsDialog
Shows work items discovered for a git branch. Grouped by type (Requirements, Change Requests, Bugs, Other) with state badges, assigned-to info, and a discovery log.

---

## 12. Styling & Theming

### CSS Architecture

**File:** `wwwroot/app.css` (~1640 lines)

Organized into numbered sections:
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
16. Daily Summary — Day summary text, conversation panels, tool chips, token chart

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
<Version>1.2.0</Version>
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
| `set x.y.z`      | Set explicit version                           |
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
Builds the solution. Sets `MSBuildSDKsPath` to ensure .NET 9 SDK is used.

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
| `ClaudeTraceHub.sln`   | VS solution file (single project)               |
| `Directory.Build.props` | Centralized version (1.2.0), author, description|
| `global.json`           | Pins SDK to 9.0.308                             |
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
| `appsettings.json`                | Logging, API version, branch patterns|
| `appsettings.Development.json`    | DetailedErrors for dev mode          |
| `usersettings.json`               | User-saved Azure DevOps connection   |
| `Properties/launchSettings.json`  | VS launch profiles (port 5204)       |

### Models (6 files)

| File                       | Key Types                                              |
|----------------------------|--------------------------------------------------------|
| `AzureDevOpsSettings.cs`  | `AzureDevOpsSettings` (config binding)                 |
| `ConversationModels.cs`   | `Conversation`, `ConversationMessage`, `ToolUsageInfo`, `FileChangeTimeline`, `DiffLine`, `DiffHunk` |
| `DailySummaryModels.cs`   | `DailyStats`, `DailyConversationDetail`, `DailyFileActivity`, `HourlyTokenUsage`, `DailyActivityPoint` |
| `DashboardModels.cs`      | `DashboardStats`, `ConversationsPerDay`, `ModelDistribution` |
| `JsonlModels.cs`          | `JsonlEntry`, `JsonlMessage`, `JsonlContentBlock`, `SessionsIndex` |
| `TfsModels.cs`            | `TfsWorkItem`, `TfsQueryResult`, `WorkItemScanResult`, `ScanProgress` |

### Services (12 files)

| File                            | Lifetime    | Purpose                         |
|---------------------------------|-------------|----------------------------------|
| `ClaudeDataDiscoveryService.cs` | Singleton   | Discovers projects & sessions    |
| `JsonlParserService.cs`        | Singleton   | Parses JSONL files               |
| `ConversationCacheService.cs`  | Singleton   | Memory cache with invalidation   |
| `DataRefreshService.cs`        | Singleton+Hosted | FileSystemWatcher for live updates |
| `SettingsService.cs`           | Singleton   | Writes usersettings.json         |
| `DailySummaryService.cs`       | Scoped      | Daily analytics & summary        |
| `DashboardService.cs`          | Scoped      | Dashboard data aggregation       |
| `ExcelExportService.cs`        | Scoped      | Excel workbook generation        |
| `ThemeService.cs`              | Scoped      | Theme + dark mode state          |
| `TfsWorkItemFilterService.cs`  | Scoped      | Branch scan orchestration        |
| `AzureDevOpsService.cs`        | HttpClient  | Azure DevOps REST API            |
| `LineDiffHelper.cs`            | Static      | LCS diff algorithm               |

### Components (15 files)

| File                          | Route                                | Purpose                    |
|-------------------------------|--------------------------------------|----------------------------|
| `App.razor`                   | -                                    | Root HTML document         |
| `Routes.razor`                | -                                    | Router config              |
| `_Imports.razor`              | -                                    | Global usings              |
| `MainLayout.razor`            | -                                    | App shell                  |
| `NavMenu.razor`               | -                                    | Sidebar nav                |
| `Home.razor`                  | `/`                                  | Dashboard                  |
| `Projects.razor`              | `/projects`                          | Project listing            |
| `ProjectDetail.razor`         | `/project/{ProjectDirName}`          | Project sessions           |
| `ConversationViewer.razor`    | `/conversation/{proj}/{session}`     | Full conversation          |
| `DailySummary.razor`          | `/daily-summary`, `/daily-summary/{date}` | Daily analytics     |
| `TfsWorkItemExplorer.razor`   | `/tfs-explorer`                      | TFS work item explorer     |
| `Settings.razor`              | `/settings`                          | Azure DevOps settings      |
| `FileChangeDialog.razor`      | - (dialog)                           | GitHub-style diff viewer   |
| `TfsWorkItemsDialog.razor`    | - (dialog)                           | Branch work items          |
| `Error.razor`                 | `/Error`                             | Error page                 |

### Static Assets

| File                  | Description                                      |
|-----------------------|--------------------------------------------------|
| `wwwroot/app.css`     | Global styles (~1640 lines, 16 sections)         |
| `wwwroot/js/download.js` | JS interop: creates blob download from byte array |
| `wwwroot/favicon.png` | Application icon                                 |

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
    │                      │──────────► Daily Summary
    │  Azure DevOps API ◄──│──────────► TFS Explorer
    │                      │──────────► Settings
    └──────────┬───────────┘
               │
               ▼
    http://localhost:5000
    (Blazor Server + SignalR)
```
