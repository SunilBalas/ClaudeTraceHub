# CLAUDE.md

Guidance for Claude Code when working in this repository. See [PROJECT_WALKTHROUGH.md](PROJECT_WALKTHROUGH.md) for full architecture, model, and page reference.

## Project Snapshot

- **Type:** .NET 9.0 Blazor Server app (single project: `ClaudeTraceHub.Web`)
- **Render mode:** `@rendermode InteractiveServer` on every page
- **UI:** MudBlazor 8.15.0 (Material Design)
- **Other libs:** ClosedXML 0.105.0 (Excel export), Markdig 0.44.0 (Markdown render)
- **External APIs:** Azure DevOps / TFS REST (v5.0), Anthropic profile API (Settings → Claude Account)
- **Data source:** Claude Code JSONL files under `~/.claude/projects/`

## Build & Run

Use the `scripts/*.bat` wrappers — they pin the .NET 9 SDK path so MSBuild picks the right version.

```bash
scripts\restore.bat      # restore packages
scripts\build.bat        # dotnet build ClaudeTraceHub.sln
scripts\run.bat          # http://localhost:5000 (auto-opens browser)
scripts\clean.bat        # dotnet clean
```

`tracehub.bat publish` produces a self-contained `win-x64` build under `publish/`.

### SDK path gotcha

`scripts/build.bat`, `clean.bat`, `restore.bat`, `run.bat` all set:

```
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.313\Sdks
```

If a build fails with "SDK not found", check the actual installed version under
`C:\Program Files\dotnet\sdk\` and update all four scripts plus
[PROJECT_WALKTHROUGH.md](PROJECT_WALKTHROUGH.md) `§3` and `§14` to match.
`global.json` rolls forward (`"rollForward": "latestMajor"`), so it does **not**
need to be edited for routine SDK bumps.

## Repository Layout (top hits)

```
ClaudeTraceHub.Web/
├── Program.cs                # Service registration & pipeline
├── appsettings.json          # NavMenu order, AzureDevOps defaults
├── usersettings.json         # User-saved org URL / PAT / projects (live-reload)
├── Models/                   # POCOs only (no logic)
├── Services/                 # Business logic
└── Components/
    ├── Layout/               # MainLayout, NavMenu (config-driven)
    └── Pages/                # All routed pages + dialogs
scripts/                      # build/clean/restore/run/bump-version
Directory.Build.props         # <Version> source of truth
```

## Service Lifetime Conventions

When adding a new service, register it in `Program.cs` and pick a lifetime that
matches existing patterns:

- **Singleton** — file-system / cache services and stateless helpers that don't
  depend on per-request data (`ClaudeDataDiscoveryService`, `JsonlParserService`,
  `ConversationCacheService`, `DataRefreshService`, `SettingsService`,
  `ClaudeAccountService`).
- **Scoped** — pure-aggregator services that read singletons and produce
  per-page bundles (`DashboardService`, `UsageStatisticsService`,
  `ExcelExportService`, `ThemeService`, `TfsWorkItemFilterService`).
- **HttpClient (typed)** — anything that calls a remote REST API. Use
  `builder.Services.AddHttpClient<TService>()` so `IHttpClientFactory` manages
  the socket pool. Existing examples: `AzureDevOpsService`,
  `AiAdoptionService`, `TfsEfficiencyService`. Each constructor builds
  the base URL + Basic auth header from `IOptionsSnapshot<AzureDevOpsSettings>`.

`DataRefreshService` is registered as both a singleton and an `IHostedService`
via the two-line pattern in `Program.cs` — keep it that way; pages subscribe to
its `OnDataChanged` event for live refresh.

## Page Conventions

- Every routable page starts with `@page "/route"` and `@rendermode InteractiveServer`.
- For CPU-bound work, wrap the call in `Task.Run(() => ...)` and `await` it so
  the SignalR circuit stays responsive.
- Pages that depend on Azure DevOps must check the typed service's
  `IsConfigured` property and render a `MudAlert` linking to `/settings` when
  unconfigured (see `AiAdoption.razor`, `TfsEfficiency.razor`).
- For "expand a member to see drilldown" UX, the existing pattern is a single
  nullable `_expandedMember` (or similar) string field plus a `Toggle…Expand`
  method. Don't reach for `Dictionary<string, bool>` — match the existing
  shape in `AiAdoption.razor` / `TfsEfficiency.razor`.

## NavMenu (Config-Driven)

Sidebar entries are declared once inside `NavMenu.razor`'s `_items` dictionary
and ordered by `appsettings.json` → `NavMenu.Order`. To add a new page to the
nav:

1. Add a `MudNavLink`-equivalent entry to `_items` with the icon, route, and
   `RequiresSetupGate` flag (true if the page needs Azure DevOps configured).
2. Add the same key to `NavMenu.Order` in `appsettings.json` at the desired
   position.

Missing keys in config get appended automatically — never silently dropped.
The `Settings` link is always rendered below the divider and is not part of
the registry.

## Configuration

- `appsettings.json` — base defaults (logging, NavMenu order, branch regex
  patterns, Azure DevOps API version).
- `usersettings.json` — user-saved Azure DevOps connection (URL, PAT, projects).
  Loaded with `reloadOnChange: true` and written by `SettingsService`. Treat
  `PersonalAccessToken` as a secret; never log it.
- Read settings via `IOptionsSnapshot<AzureDevOpsSettings>` (per-request) for
  scoped/transient code, or `IOptionsMonitor<>` if you need change callbacks.

## Styling & Theming

- Global CSS lives in [`ClaudeTraceHub.Web/wwwroot/app.css`](ClaudeTraceHub.Web/wwwroot/app.css)
  in numbered sections (`/* 1. BACKGROUND */`, `/* 13. TFS WORK ITEM EXPLORER */`,
  etc.). When adding styles for a new feature, add a new numbered section —
  do not interleave into existing ones.
- All custom selectors must respect `.light-theme` and `.dark-theme` prefixes
  on `<html>`. The class is applied from `localStorage` before Blazor boots to
  avoid a flash.
- 4 theme palettes (Purple, Ocean Blue, Forest Green, Sunset) are managed by
  `ThemeService`; they replace `MudBlazor`'s default `MudThemeProvider` palette.

## Versioning & Commit Style

- Single source of truth: `Directory.Build.props` → `<Version>`. Currently `1.9.0`.
- Bump with `scripts\bump-version.bat <auto|major|minor|patch|set x.y.z>`.
  `auto` infers the bump from conventional-commit prefixes since the last
  `v*` tag.
- Commit messages on this repo use lowercase conventional commits:
  `feat: …`, `fix: …`, `chore: …`, `refactor: …`, `docs: …`. Optional scope
  in parentheses (`feat(settings): …`). The `bump-version.bat auto` parser
  reads these prefixes — keep them clean.

## When You're Working on…

- **Conversation parsing** — see `JsonlParserService.cs`. It has two modes
  (`ScanMetadata` for fast lists, `ParseFile` for full detail). Cache results
  through `ConversationCacheService` instead of re-parsing.
- **Per-member task drilldowns** (AI Adoption / TFS Efficiency) — the data
  shape is built service-side (`MemberAdoptionStats.RawWorkItems`,
  `MemberDailyEfficiency.Tasks`). Don't recompute member task lists in the
  Razor markup; ask the service to expose the field.
- **TFS comments rendering** — `TfsEfficiency.razor`'s `FormatComment` parses a
  trailing `DD/MM/YYYY, Total Hours: X.Xh — bullet1. bullet2.` shape. If
  comments stop rendering correctly, that regex is the first place to look.
- **Claude account info** — `ClaudeAccountService` reads
  `~/.claude/.credentials.json` and tries to enrich via `https://claude.ai/api/oauth/profile`.
  It caches for 5 min on success and 1 min on network failure. Don't strip the
  `User-Agent: claude-cli/...` header — the profile endpoint requires it.

## Don'ts

- Don't add new pages without listing them in `NavMenu.razor`'s `_items` and
  `appsettings.json` → `NavMenu.Order`.
- Don't introduce a fourth service-lifetime category — fit new services into
  Singleton / Scoped / HttpClient.
- Don't read `~/.claude` paths directly from pages or other services — go
  through `ClaudeDataDiscoveryService` (sessions) or `ClaudeAccountService`
  (credentials).
- Don't hand-roll a `HttpClient` for new TFS endpoints — add a typed
  `HttpClient` service so auth and base-URL setup stay in one place.
- Don't hard-code colors. Use MudBlazor `Color.*` enums or
  `var(--mud-palette-*)` CSS vars so themes keep working.
