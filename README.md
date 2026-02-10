# ClaudeTraceHub

A .NET 9.0 Blazor Server application for browsing, analyzing, and exporting Claude Code conversation traces with Azure DevOps/TFS work item integration.

ClaudeTraceHub automatically discovers conversation data from `~/.claude/projects/` and presents it through an interactive dashboard with activity charts, project browsing, full conversation viewing with GitHub-style diffs, and TFS work item linking.

## Features

- **Dashboard** — Activity charts (conversations per day, model usage distribution), stat cards, top projects, and recent conversation list
- **Project Browser** — Searchable, sortable listing of all discovered Claude Code projects with session counts and last activity
- **Conversation Viewer** — Full conversation timeline with markdown rendering, tool usage details, token metadata, and files touched panel
- **File Change Diffs** — GitHub-style unified diffs showing step-by-step file modifications made during conversations
- **TFS Work Item Explorer** — Links conversations to Azure DevOps work items via PR matching and branch name pattern extraction
- **Excel Export** — Export any conversation to `.xlsx` with structured message and metadata sheets
- **Live Refresh** — FileSystemWatcher detects new conversation data and refreshes the UI automatically
- **Theming** — 4 color themes (Purple, Ocean Blue, Forest Green, Sunset) with light/dark mode toggle

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download) (9.0.308 or compatible)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed and used (generates `~/.claude/projects/` data)
- Azure DevOps or TFS instance

## Quick Start

```bash
# Restore packages
scripts\restore.bat

# Build
scripts\build.bat

# Run
scripts\run.bat
```

The app launches at **http://localhost:5000** (HTTPS on port 5001).

On first run, a setup wizard guides you through configuring Azure DevOps settings (optional — conversation browsing works without it).

## CLI Tool (tracehub.bat)

```bash
tracehub.bat publish    # Build self-contained win-x64 executable
tracehub.bat run        # Start the published executable
tracehub.bat autostart  # Register in Windows Startup (auto-start on login)
tracehub.bat remove     # Remove from Windows Startup
tracehub.bat status     # Check registration and running state
tracehub.bat version    # Show current version
```

## Project Structure

```
ClaudeTraceHub/
├── ClaudeTraceHub.sln
├── tracehub.bat                        # CLI entry point
├── Directory.Build.props               # Centralized version & metadata
├── global.json                         # SDK version pin
├── scripts/                            # Dev build scripts
│   ├── build.bat
│   ├── run.bat
│   ├── restore.bat
│   ├── clean.bat
│   └── bump-version.bat                # Semantic version manager
│
└── ClaudeTraceHub.Web/
    ├── Program.cs                      # Service registration & startup
    ├── Models/                         # Data models
    │   ├── ConversationModels.cs       # Core domain (Project, Session, Message, Diff)
    │   ├── JsonlModels.cs             # JSONL deserialization
    │   ├── TfsModels.cs               # Work item models
    │   ├── DashboardModels.cs         # Dashboard statistics
    │   └── AzureDevOpsSettings.cs     # Configuration model
    ├── Services/                       # Business logic
    │   ├── ClaudeDataDiscoveryService  # Project & session discovery
    │   ├── JsonlParserService          # JSONL parsing (fast + full modes)
    │   ├── ConversationCacheService    # Memory cache with file-change invalidation
    │   ├── DataRefreshService          # FileSystemWatcher for live updates
    │   ├── DashboardService            # Dashboard aggregations
    │   ├── AzureDevOpsService          # Azure DevOps REST API client
    │   ├── TfsWorkItemFilterService    # Branch scan orchestration
    │   ├── ExcelExportService          # Excel workbook generation
    │   ├── ThemeService                # Theme management
    │   ├── SettingsService             # User settings persistence
    │   └── LineDiffHelper              # LCS-based diff algorithm
    └── Components/
        ├── Layout/                     # App shell & navigation
        └── Pages/                      # Razor pages (Home, Projects, etc.)
```

## Configuration

**appsettings.json** — Base settings including API version and branch-to-work-item regex patterns.

**usersettings.json** — Created via the Settings page. Stores Azure DevOps organization URL, PAT, and selected projects. This file is in `.gitignore` to prevent credential leaks.

Configuration layers are merged with live-reloading support:
```
appsettings.json → appsettings.Development.json → usersettings.json → IOptionsMonitor<T>
```

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 9.0 |
| Web Framework | Blazor Server (Interactive Server) |
| UI Components | MudBlazor 8.15.0 |
| Excel Export | ClosedXML 0.105.0 |
| Markdown | Markdig 0.44.0 |
| API Integration | Azure DevOps REST API v5.0 |

## Version Management

Version is centralized in `Directory.Build.props`. Use the bump script for releases:

```bash
scripts\bump-version.bat auto      # Auto-detect from git commit messages
scripts\bump-version.bat minor     # Bump minor version
scripts\bump-version.bat set 2.0.0 # Set explicit version
scripts\bump-version.bat tag       # Create git tag
```

Commit prefixes like `feat:`, `fix:`, and `breaking:` drive auto-detection.

## License

All rights reserved.
