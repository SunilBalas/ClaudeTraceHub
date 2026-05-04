# Commit Flow Guide

Conventions for branching, commits, and version bumps in **ClaudeTraceHub**, derived from the actual history (PRs #1–#7). Follow this so `scripts\bump-version.bat auto` keeps working and the changelog stays clean.

See also: [CLAUDE.md](CLAUDE.md) · [PROJECT_WALKTHROUGH.md](PROJECT_WALKTHROUGH.md)

---

## 1. Branch Creation

**Format:** `DDMMYYYY/feature-kebab-name`

The date prefix is the day you start the work, followed by a slash and a short kebab-case description.

```bash
git checkout master
git pull
git checkout -b 04052026/short-feature-name
```

Real examples from history:

- `04052026/member-task-drilldowns-and-account-info`
- `04052026/planning-verification-and-code-merging`
- `13042026/tfs-efficiency`
- `17042026/remove-daily-summary-feature`
- `24022026/cluade-usage-stats-dashboard`

---

## 2. Commit Messages (lowercase conventional commits)

Per [CLAUDE.md](CLAUDE.md), use lowercase prefixes — the `bump-version.bat auto` parser depends on them.

| Prefix                                                  | Bump type   | Use for             |
| ------------------------------------------------------- | ----------- | ------------------- |
| `breaking:` / `major:` / `BREAKING CHANGE`              | **major**   | Breaking changes    |
| `feat:` / `feature:` / `add:` / `update:` / `enhance:`  | **minor**   | New features        |
| `fix:` / `bugfix:` / `hotfix:` / `patch:` / `perf:`     | **patch**   | Bug fixes           |
| `chore:` / `refactor:` / `docs:`                        | patch (fallback) | Maintenance    |

Optional scope in parentheses: `feat(settings): add PAT validation`.

```bash
git add <files>
git commit -m "feat: add member task drilldowns and Claude account info panel"
```

### Real commits from history

```
feat: add member task drilldowns and Claude account info panel
feat: Add Code Merging Sheet, Planning Verification, and improvements
fix: improve conversation UI and branch iteration format
chore: remove daily summary feature and make navmenu config-driven
chore: bump version to 1.11.0
```

---

## 3. Version Bump (separate commit, just before opening PR)

The recurring two-commit pattern landing on `master` is:

```
feat: <the actual change>
chore: bump version to X.Y.Z
```

Run from the repo root:

```bash
scripts\bump-version.bat auto       # infers major/minor/patch from commits since last v* tag
scripts\bump-version.bat major      # breaking change
scripts\bump-version.bat minor      # new feature
scripts\bump-version.bat patch      # bug fix / hotfix
scripts\bump-version.bat set 2.0.0  # pin a specific version
scripts\bump-version.bat current    # print current version
scripts\bump-version.bat tag        # create v<current> git tag at HEAD
```

The script only edits [Directory.Build.props](Directory.Build.props) `<Version>` — it does **not** stage or commit. Do that yourself:

```bash
git add Directory.Build.props
git commit -m "chore: bump version to 1.12.0"
```

### How `auto` decides the bump

It reads commit subjects since the last `v*` git tag and picks the **highest-priority** match:

1. Any `breaking:` / `major:` / `BREAKING CHANGE` → **major** (resets minor & patch to 0)
2. Else any `feat:` / `feature:` / `add:` / `update:` / `enhance:` → **minor** (resets patch to 0)
3. Else → **patch**

If there's no `v*` tag yet, it analyses **all** commits.

---

## 4. Push & Open PR

```bash
git push -u origin 04052026/short-feature-name
gh pr create --title "feat: short feature title" --body "..."
```

After the PR merges into `master`, optionally tag the released version so the next `auto` bump has a clean range to analyse:

```bash
git checkout master && git pull
scripts\bump-version.bat tag        # creates v1.12.0 at HEAD
git push origin v1.12.0
```

---

## Quick Cheat Sheet

```bash
# 1. start
git checkout master && git pull
git checkout -b 04052026/my-feature

# 2. work
git add <files>
git commit -m "feat: short description of what changed"

# 3. bump (auto picks minor because of feat:)
scripts\bump-version.bat auto
git add Directory.Build.props
git commit -m "chore: bump version to X.Y.Z"

# 4. ship
git push -u origin 04052026/my-feature
gh pr create

# 5. tag after merge (optional but recommended)
git checkout master && git pull
scripts\bump-version.bat tag
git push origin vX.Y.Z
```

---

## Don'ts

- Don't use uppercase prefixes (`Feat:`, `[feat]:`) — `auto` regex is lowercase-anchored.
- Don't bundle the version bump into the feature commit — keep them as two separate commits so the changelog reads cleanly.
- Don't hand-edit `<Version>` in [Directory.Build.props](Directory.Build.props) — use the script so the format stays consistent.
- Don't push without a tag if you want `auto` to work on the next branch — without a `v*` tag, it re-analyses the full history.
