# G44 — Version Control for Game Dev



> **Category:** Guide · **Related:** [E4 Solo Project Management](../../core/project-management/E4_project_management.md) · [R3 Project Structure](../reference/R3_project_structure.md) · [G8 Content Pipeline](./G8_content_pipeline.md)

---

## Why This Matters

Game projects die in silence. Not from bad code — from lost code. A corrupted save, a
regretted refactor with no way back, an asset change that broke everything three days ago
and you only just noticed. Version control is your undo button for the entire project.

Even as a solo dev, Git isn't optional. It's the difference between "I can try anything
fearlessly" and "I'm afraid to touch this code because it finally works."

This guide covers Git workflows specifically tuned for MonoGame / .NET 8 game projects —
including the binary asset headaches that make gamedev version control different from
typical software projects.

---

## 1 — Git Basics for Game Dev

### Why Solo Devs Need Version Control

- **Fearless experimentation.** Try a new physics system. If it doesn't work, `git checkout` and you're back.
- **Time travel.** "The game felt better two weeks ago" — go find out exactly what changed.
- **Automatic backup.** Every push to a remote is a full backup of your project history.
- **Progress tracking.** Git log _is_ your dev diary.

### Commit Frequency

**Commit working states, not work-in-progress.** A commit should represent a point where:

- The project compiles
- The game runs (even if a feature is incomplete)
- You could hand this to someone and they could build it

Bad rhythm:
```
"end of day dump"
"stuff"
"WIP WIP WIP"
```

Good rhythm:
```
"Add player jump with coyote time (8 frames)"
"Fix tilemap collision on slopes — was using wrong edge"
"Add footstep SFX, 3 variants with random pitch"
```

**Aim for 3–10 commits per coding session.** If you're committing once a day, your commits
are too big. If you're committing every line change, you're overthinking it.

### Commit Messages That Actually Help

Write messages that future-you can scan in `git log --oneline`:

```bash
# Format: <area>: <what changed>
git commit -m "player: add wall-slide with dust particles"
git commit -m "audio: implement music crossfade between zones"
git commit -m "ui: fix health bar not updating on hit"
git commit -m "content: add tileset for cave biome (32x32, 48 tiles)"
git commit -m "perf: batch sprite draws, 800→200 draw calls"
```

Prefixes that work well for games:
- `player:`, `enemy:`, `npc:` — entity-specific changes
- `physics:`, `collision:` — simulation
- `audio:`, `music:`, `sfx:` — sound
- `ui:`, `hud:`, `menu:` — interface
- `content:`, `asset:` — art/data additions
- `level:`, `map:` — level design
- `perf:` — optimization
- `fix:` — bug fixes
- `refactor:` — restructuring without behavior change

---

## 2 — .gitignore for MonoGame

A proper `.gitignore` is critical. MonoGame projects generate a lot of intermediate files
you don't want in your repo.

### Complete .gitignore

```gitignore
# =============================================================================
# .gitignore for MonoGame (.NET 8) Projects
# =============================================================================

# --- Build Output ---
bin/
obj/

# --- IDE / Editor ---
.vs/
.vscode/.history/
*.user
*.suo
*.userprefs
*.sln.docstates

# --- Rider ---
.idea/
*.sln.iml

# --- MonoGame Content Pipeline (MGCB) Intermediates ---
# The pipeline rebuilds these from source assets.
# Content/bin/ and Content/obj/ hold compiled .xnb files and intermediate data.
Content/bin/
Content/obj/

# --- OS Junk ---
.DS_Store
.DS_Store?
._*
Thumbs.db
ehthumbs.db
Desktop.ini
$RECYCLE.BIN/

# --- NuGet (restored on build) ---
# Uncomment if you don't commit packages (recommended):
# packages/

# --- Secrets (NEVER commit these) ---
*.env
.env.*
appsettings.*.json
secrets.json

# --- Crash Dumps / Logs ---
*.log
*.dmp
crash_reports/
```

### What TO Track

These files **must** be in your repo:

| File | Why |
|------|-----|
| `*.csproj` | Project definition — NuGet refs, build config |
| `*.sln` | Solution file — project structure |
| `Content.mgcb` | Content pipeline manifest — tells MGCB what to build and how |
| `Content/*.png` | Source textures (or use LFS — see §3) |
| `Content/*.ase` | Aseprite source files (LFS recommended) |
| `Content/*.tmx`, `*.tsx` | Tiled map/tileset files |
| `Content/*.ogg`, `*.wav` | Audio source files (LFS recommended) |
| `Content/*.spritefont` | Font descriptions |
| `*.cs` | All your source code |
| `.gitignore` | This file |
| `.gitattributes` | LFS tracking rules |

**The rule:** Track everything needed to build the project from scratch. If `dotnet build`
and the MGCB pipeline can reconstruct it, don't track it.

---

## 3 — Git LFS (Large File Storage)

### The Problem

Git stores every version of every file forever. For text files (code, XML, JSON), this is
efficient — Git uses delta compression. For binary files (images, audio, fonts), every
change stores a full copy. A 50 MB texture atlas edited 20 times = ~1 GB of history.

### Installation

```bash
# macOS
brew install git-lfs

# Windows (if not bundled with Git for Windows)
# Download from https://git-lfs.com

# Linux (Debian/Ubuntu)
sudo apt install git-lfs

# Initialize LFS in your user config (once per machine)
git lfs install
```

### Setting Up LFS for Your Project

Create `.gitattributes` in your repo root:

```gitattributes
# =============================================================================
# .gitattributes — Git LFS Tracking for Game Assets
# =============================================================================

# --- Textures & Sprites ---
*.png filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text
*.ase filter=lfs diff=lfs merge=lfs -text
*.aseprite filter=lfs diff=lfs merge=lfs -text
*.bmp filter=lfs diff=lfs merge=lfs -text
*.tga filter=lfs diff=lfs merge=lfs -text
*.gif filter=lfs diff=lfs merge=lfs -text

# --- Audio ---
*.wav filter=lfs diff=lfs merge=lfs -text
*.ogg filter=lfs diff=lfs merge=lfs -text
*.mp3 filter=lfs diff=lfs merge=lfs -text
*.flac filter=lfs diff=lfs merge=lfs -text

# --- Fonts ---
*.ttf filter=lfs diff=lfs merge=lfs -text
*.otf filter=lfs diff=lfs merge=lfs -text

# --- Video / Cutscenes (if any) ---
*.mp4 filter=lfs diff=lfs merge=lfs -text
*.webm filter=lfs diff=lfs merge=lfs -text

# --- Other Binary Assets ---
*.zip filter=lfs diff=lfs merge=lfs -text
*.7z filter=lfs diff=lfs merge=lfs -text
```

Then commit the `.gitattributes`:

```bash
git add .gitattributes
git commit -m "config: add Git LFS tracking for binary assets"
```

### Migrating an Existing Repo to LFS

If you already have binary files in regular Git history:

```bash
# Migrate existing PNG files to LFS (rewrites history!)
git lfs migrate import --include="*.png,*.wav,*.ogg,*.ase,*.ttf" --everything

# Force push after migration (coordinate if collaborating)
git push --force-with-lease
```

> ⚠️ **This rewrites history.** Only do this if you're the sole contributor, or coordinate
> with your team first.

### What to LFS-Track vs Regular Git

| LFS (binary, changes = full copies) | Regular Git (text, delta-friendly) |
|--------------------------------------|------------------------------------|
| `.png`, `.bmp`, `.tga`, `.psd` | `.cs` (source code) |
| `.ase`, `.aseprite` | `.csproj`, `.sln` |
| `.wav`, `.ogg`, `.mp3` | `.mgcb` (XML-based manifest) |
| `.ttf`, `.otf` | `.tmx`, `.tsx` (Tiled XML) |
| `.zip`, `.7z` | `.json`, `.xml`, `.yaml` |
| Large reference docs / PDFs | `.gitignore`, `.gitattributes` |
| | `.spritefont` (XML) |

**Rule of thumb:** If you can open it in a text editor and read it, regular Git. If it's
binary gibberish, LFS.

### Storage & Bandwidth Considerations

- **GitHub Free:** 1 GB LFS storage, 1 GB/month bandwidth
- **GitHub Pro:** 2 GB storage, 2 GB/month bandwidth
- **Data packs:** $5/month for 50 GB storage + 50 GB bandwidth
- **GitLab Free:** 5 GB total repo size (including LFS)

For a typical indie game:
- Small project (< 500 MB assets): Free tier is fine
- Medium project (1–5 GB assets): Budget ~$5/month or self-host
- Large project (5+ GB): Consider self-hosted Git or dedicated asset storage

Check your LFS usage:

```bash
# See what LFS is tracking
git lfs ls-files

# Check local LFS storage size
git lfs status

# See LFS usage summary
du -sh .git/lfs/
```

---

## 4 — Branching Strategy

### For Solo Devs: Keep It Simple

**Trunk-based development** works best for solo projects. That means:

- Work directly on `main`
- Commit frequently
- Every commit on `main` should compile and run

That's it. You don't need `develop`, `staging`, `release/v1.2`, or any of that. You're
one person. The ceremony of branch management costs more than it saves.

```bash
# Your daily workflow:
git add -A
git commit -m "enemy: add patrol path with waypoints"
git push
```

### When Branches Actually Help

Use a branch when you're doing something **risky or experimental** that might take multiple
sessions and could leave `main` broken:

```bash
# Experimenting with a new rendering approach
git checkout -b experiment/deferred-lighting

# ... work for a few days ...

# It worked! Merge it back.
git checkout main
git merge experiment/deferred-lighting
git branch -d experiment/deferred-lighting

# It didn't work. Just delete it.
git checkout main
git branch -D experiment/deferred-lighting
```

Good reasons to branch:
- **Experimental features** — new physics engine, renderer rewrite
- **Game jam entries** — branch off, jam for 48h, merge or abandon
- **Pre-release stabilization** — freeze features, only fix bugs
- **Risky refactors** — restructuring core systems

Bad reasons to branch:
- "Best practices say so" — best practices are for teams
- Every single feature — overhead isn't worth it solo
- Matching some corporate Git flow diagram

### Merge vs Rebase (Solo)

For solo work, it barely matters. But here's the quick version:

```bash
# Merge: preserves branch history (creates a merge commit)
git checkout main
git merge feature/new-enemy

# Rebase: linear history (replays commits on top of main)
git checkout feature/new-enemy
git rebase main
git checkout main
git merge feature/new-enemy  # fast-forward, no merge commit
```

**Opinion:** Use merge. It's simpler, safer, and the "messy" merge commits don't matter
when you're the only one reading the log. Rebase is nice for clean history but introduces
risk of conflicts and mistakes for minimal solo benefit.

---

## 5 — Asset Management

### Source Assets vs Built Assets

Your repo stores **source assets**. The content pipeline builds them into runtime formats.

```
Source (tracked in Git)          Built (NOT tracked — .gitignore'd)
─────────────────────────        ──────────────────────────────────
Content/Sprites/player.ase   →  Content/bin/Sprites/player.xnb
Content/Maps/level1.tmx      →  Content/bin/Maps/level1.xnb
Content/Audio/jump.wav       →  Content/bin/Audio/jump.xnb
Content/Fonts/ui.spritefont  →  Content/bin/Fonts/ui.xnb
```

**Never track `Content/bin/` or `Content/obj/`.** These are rebuilt by MGCB every build.
Tracking them means constant merge conflicts on binary `.xnb` files and bloated repos.

### What Source Assets to Track

```
Content/
├── Content.mgcb          ← TRACK (pipeline manifest, text-based XML)
├── Sprites/
│   ├── player.ase        ← TRACK via LFS (Aseprite source)
│   ├── player.png        ← TRACK via LFS (exported spritesheet)
│   └── enemies.ase       ← TRACK via LFS
├── Tilesets/
│   ├── cave.ase          ← TRACK via LFS (tileset source)
│   ├── cave.png          ← TRACK via LFS (exported tileset)
│   └── cave.tsx          ← TRACK in regular Git (Tiled XML)
├── Maps/
│   ├── level1.tmx        ← TRACK in regular Git (Tiled XML)
│   └── level2.tmx        ← TRACK in regular Git
├── Audio/
│   ├── music_forest.ogg  ← TRACK via LFS
│   ├── sfx_jump.wav      ← TRACK via LFS
│   └── sfx_jump.audacity ← TRACK via LFS (Audacity project, if you keep it)
├── Fonts/
│   ├── pixel.ttf         ← TRACK via LFS
│   └── ui.spritefont     ← TRACK in regular Git (XML description)
└── Data/
    ├── enemies.json      ← TRACK in regular Git
    └── items.csv         ← TRACK in regular Git
```

### The Aseprite Workflow

If you use Aseprite, track **both** the `.ase` source and the exported `.png`:

```bash
# In your export script or Makefile:
aseprite -b Content/Sprites/player.ase --sheet Content/Sprites/player.png

# Commit both:
git add Content/Sprites/player.ase Content/Sprites/player.png
git commit -m "asset: update player walk cycle (6→8 frames)"
```

Why both? The `.ase` is your editable source (layers, frames, tags). The `.png` is what
MGCB actually imports. Keeping both means anyone can build the project without Aseprite
installed, while you retain full editing capability.

### Content Pipeline Rebuild

A clean checkout should build everything:

```bash
git clone https://github.com/you/your-game.git
cd your-game
dotnet restore
dotnet build    # MGCB rebuilds all content from source assets
dotnet run      # Game runs
```

If this doesn't work, you're missing source assets in your repo.

---

## 6 — Commit Strategies for Games

### Atomic Commits

One logical change per commit. Not "everything I did today."

```bash
# GOOD: atomic commits
git add Entities/Player.cs Content/Sprites/player.png
git commit -m "player: add double-jump with squash animation"

git add Systems/PhysicsSystem.cs
git commit -m "physics: fix tunneling on thin platforms"

git add Screens/MainMenu.cs Content/UI/menu_bg.png
git commit -m "ui: add animated main menu background"

# BAD: dump commit
git add -A
git commit -m "lots of changes"
```

Why bother? Because when the double-jump introduces a bug, you can revert that one commit
without losing the physics fix and the menu work.

### Staging Selectively

Use `git add -p` (patch mode) to commit parts of a file:

```bash
# Stage specific hunks from a file
git add -p Systems/PhysicsSystem.cs

# Or stage specific files, not everything
git add Entities/Player.cs Entities/PlayerStates/JumpState.cs
git commit -m "player: add wall-jump state"
```

### Tagging Milestones

Tags mark important points: playable builds, demo versions, jam submissions.

```bash
# Tag a playable build
git tag -a v0.1.0 -m "First playable: movement, combat, 3 levels"

# Tag a jam submission
git tag -a jam-ld55 -m "Ludum Dare 55 submission"

# Tag a demo release
git tag -a demo-2025-03 -m "March 2025 demo build"

# Push tags to remote
git push --tags

# List all tags
git tag -l

# Checkout a tagged version
git checkout v0.1.0
```

### Versioning Scheme

For indie games, keep it simple:

```
v0.1.0  — First playable (core loop works)
v0.2.0  — Major feature milestone (combat, inventory, etc.)
v0.3.0  — Content milestone (all levels blocked out)
...
v0.9.0  — Feature complete
v1.0.0  — Release
v1.0.1  — Post-release patch
v1.1.0  — Content update
```

You can read the current tag in your game for a version display:

```bash
# Get current version string for your build
git describe --tags --always
# Output: v0.3.0-14-g2f8a9c1
# Meaning: 14 commits after v0.3.0, at commit 2f8a9c1
```

---

## 7 — Backup & Remote

### Setting Up a Remote

```bash
# Create repo on GitHub (use gh CLI or web UI)
gh repo create my-game --private --source=. --remote=origin

# Or add remote manually
git remote add origin git@github.com:yourname/my-game.git
git push -u origin main
```

**Always use private repos for unreleased games.** You can make it public later. You can't
un-leak your source code.

### Push Frequency

Push every time you're done working. At minimum, push daily. Your local machine is a
single point of failure — hard drives die, laptops get stolen, cats walk on keyboards.

```bash
# End of session ritual:
git add -A
git status                    # Review what you're committing
git commit -m "enemy: patrol AI follows waypoints"
git push
```

### Multiple Remotes for Redundancy

Belt and suspenders. Push to two places:

```bash
# Add a second remote (GitLab as backup)
git remote add backup git@gitlab.com:yourname/my-game.git

# Push to both
git push origin main
git push backup main

# Or create an alias to push everywhere
git remote add all git@github.com:yourname/my-game.git
git remote set-url --add --push all git@github.com:yourname/my-game.git
git remote set-url --add --push all git@gitlab.com:yourname/my-game.git

# Now `git push all main` pushes to both
```

### Automated Push Script

Save yourself the keystrokes:

```bash
#!/bin/bash
# save-and-push.sh — Quick save with auto-generated message
set -e

if [ -z "$(git status --porcelain)" ]; then
    echo "Nothing to commit."
    exit 0
fi

git add -A
git status --short
echo ""
read -p "Commit message: " msg
git commit -m "$msg"
git push origin main
echo "✅ Pushed to origin."
```

---

## 8 — Common Pitfalls

### 1. Committing Build Artifacts

**Problem:** `bin/` and `obj/` in your repo. Hundreds of MBs of compiled garbage.

**Fix:** Add them to `.gitignore` before your first commit. If they're already tracked:

```bash
# Remove from tracking but keep local files
git rm -r --cached bin/ obj/ Content/bin/ Content/obj/
git commit -m "config: remove build artifacts from tracking"
```

### 2. Forgetting Git LFS

**Problem:** You tracked `.png` and `.wav` files in regular Git for months. Your repo is
now 2 GB and cloning takes 20 minutes.

**Fix:** Migrate to LFS (rewrites history):

```bash
git lfs migrate import --include="*.png,*.wav,*.ogg,*.ase,*.ttf" --everything
git push --force-with-lease
```

**Prevention:** Set up `.gitattributes` with LFS rules in your project template before
you start.

### 3. Massive Repos from Binary Bloat

**Problem:** You keep re-exporting the same spritesheet with small changes. Git stores
every version as a full copy.

**Mitigations:**
- Use Git LFS (stores binaries outside the repo's main object store)
- Export less frequently — batch art changes
- Use texture atlases that change less often
- Consider a separate asset repo for very large projects

### 4. Merge Conflicts in Content.mgcb

**Problem:** `Content.mgcb` is XML-based and Git doesn't merge XML well.

**Mitigations:**
- With solo trunk-based development, this rarely happens
- If it does: open in MGCB Editor, re-add the conflicting assets, save
- Keep `Content.mgcb` entries sorted (the MGCB Editor doesn't guarantee order, but
  you can manually sort — makes diffs cleaner)

### 5. Accidentally Committing Secrets

**Problem:** API keys, Steam credentials, analytics tokens in your repo.

**Prevention:**
```bash
# Use .env files (gitignored) for secrets
echo "STEAM_API_KEY=abc123" > .env
echo ".env" >> .gitignore

# If you already committed a secret:
# 1. Rotate the key immediately (it's compromised)
# 2. Remove from history:
git filter-branch --force --index-filter \
  "git rm --cached --ignore-unmatch secrets.json" \
  --prune-empty --tag-name-filter cat -- --all
git push --force-with-lease
```

> ⚠️ If a secret was pushed to a public repo even briefly, consider it compromised.
> Rotate the key. Rewriting history doesn't help if someone already cloned it.

### 6. "It Works on My Machine"

**Problem:** You forgot to commit a file. Everything works locally but a fresh clone fails.

**Prevention:** Periodically test a clean clone:

```bash
cd /tmp
git clone git@github.com:yourname/my-game.git test-clone
cd test-clone
dotnet build
dotnet run
# If this fails, you're missing something in your repo.
rm -rf /tmp/test-clone
```

---

## 9 — Git Bisect for Game Bugs

### The Scenario

"The player's jump feels wrong. It was fine last week. Which commit broke it?"

With 50+ commits since then, manually checking each one is insane. `git bisect` does a
binary search through your history to find the exact commit.

### Manual Bisect

```bash
# Start bisecting
git bisect start

# Current commit is bad (jump is broken)
git bisect bad

# Tag a known-good commit (last Tuesday, jump was fine)
git bisect good v0.2.0
# Or use a commit hash: git bisect good a1b2c3d

# Git checks out a middle commit. Test the game.
# If jump works:
git bisect good
# If jump is broken:
git bisect bad

# Repeat 5-6 times. Git narrows it down:
# "abc1234 is the first bad commit"
# commit abc1234
# Author: you
# Date: Thursday
# "physics: change gravity curve for better feel"
#
# AH HA. That's the one.

# Done — go back to main
git bisect reset
```

### Automated Bisect

If you have a test script that can detect the bug:

```bash
# test-jump.sh — exits 0 if jump works, 1 if broken
#!/bin/bash
dotnet build -c Release --nologo -v q 2>/dev/null
# Run a headless test or check a specific value
dotnet run --project Tests/ -- --test JumpHeightTest
```

```bash
# Automated bisect — Git runs the script at each step
git bisect start
git bisect bad HEAD
git bisect good v0.2.0
git bisect run ./test-jump.sh

# Git finds the bad commit automatically.
git bisect reset
```

This is why compiling commits matter — bisect only works if each commit builds.

---

## 10 — Collaboration

### When You're No Longer Solo

If someone joins your project (artist, musician, second programmer), upgrade your workflow:

### Branch Protection

```bash
# On GitHub: Settings → Branches → Add rule
# Branch name pattern: main
# ✅ Require pull request reviews before merging
# ✅ Require status checks to pass (if you have CI)
```

### PR Workflow

```bash
# Contributor creates a feature branch
git checkout -b feature/enemy-ai
# ... work ...
git push origin feature/enemy-ai

# Open a PR on GitHub
gh pr create --title "Add enemy patrol AI" --body "Enemies follow waypoint paths..."

# Review, discuss, merge via GitHub UI or:
gh pr merge --squash
```

### Code Review for Game Code

Game code review priorities (different from business software):

1. **Does it break existing gameplay?** — Regressions in feel are hard to catch in review
2. **Performance implications** — Will this run 60× per second? Allocations in update loops?
3. **State management** — Are game states clean? Can this leak between scenes?
4. **Magic numbers** — Is `0.85f` a tuning value that should be in a config file?
5. **Asset references** — Are content paths correct? Will MGCB find these files?

### Handling Shared Content.mgcb

The MGCB file is a frequent conflict source with multiple contributors:

```bash
# Option 1: One person "owns" Content.mgcb and merges asset additions
# Option 2: Use a script to regenerate Content.mgcb from directory contents
# Option 3: Accept occasional manual conflict resolution
```

For small teams, Option 1 (designated content wrangler) works best.

### Git Hooks for Teams

```bash
# .githooks/pre-commit — prevent committing build artifacts
#!/bin/bash
if git diff --cached --name-only | grep -qE '^(bin|obj|Content/bin|Content/obj)/'; then
    echo "ERROR: Build artifacts staged. Remove bin/obj from commit."
    exit 1
fi
```

```bash
# Enable the hooks directory
git config core.hooksPath .githooks
```

---

## 11 — Recovery

### Recovering Deleted Files

```bash
# File was deleted in a recent commit — find which one:
git log --diff-filter=D --summary -- "Content/Sprites/old_player.png"

# Restore it from the commit before deletion:
git checkout abc1234^ -- "Content/Sprites/old_player.png"
```

### Reverting a Bad Commit

```bash
# Undo the last commit but keep changes staged:
git reset --soft HEAD~1

# Undo the last commit and unstage changes (keep files):
git reset --mixed HEAD~1

# Nuclear option — undo last commit and discard all changes:
git reset --hard HEAD~1

# Safer: create a new commit that undoes a specific commit
# (preserves history — use this if you already pushed)
git revert abc1234
```

### Stashing WIP

You're mid-feature but need to switch to fix a bug:

```bash
# Save current work without committing
git stash push -m "WIP: player dash ability"

# Fix the bug on main
git checkout main
# ... fix ...
git commit -m "fix: prevent crash when enemy count is zero"
git push

# Come back to your work
git stash pop
# Your WIP changes are restored.
```

```bash
# List all stashes
git stash list

# Apply a specific stash without removing it
git stash apply stash@{2}

# Drop a stash you no longer need
git stash drop stash@{0}
```

### Cherry-Picking Fixes

You fixed a bug on a feature branch and need it on `main` right now:

```bash
# Find the commit hash of the fix
git log --oneline feature/new-enemy
# abc1234 fix: enemy spawner null check

# Apply just that commit to main
git checkout main
git cherry-pick abc1234
git push
```

### The Reflog — Your Safety Net

Even after `reset --hard`, Git keeps a reflog of where HEAD has been:

```bash
# See recent HEAD positions
git reflog

# Output:
# abc1234 HEAD@{0}: reset: moving to HEAD~1
# def5678 HEAD@{1}: commit: player: add dash ability
# ...

# Recover the "lost" commit
git checkout def5678
# Or reset main back to it:
git reset --hard def5678
```

The reflog keeps entries for ~90 days by default. It's your last line of defense.

### "I Broke Everything" Emergency Playbook

1. **Don't panic.** Git almost never truly loses data.
2. `git reflog` — find the last known good state
3. `git stash` — if you have uncommitted changes you want to keep
4. `git reset --hard <good-commit>` — go back to safety
5. `git push --force-with-lease` — update remote (only if you're solo!)

---

## Quick Reference

### Daily Workflow (Solo)

```bash
# Start of session
git pull                    # Get any changes (multi-machine setup)

# While working
git add <files>
git commit -m "area: description"
# Repeat for each logical change

# End of session
git push
```

### New Project Setup

```bash
mkdir MyGame && cd MyGame
dotnet new mgdesktopgl -n MyGame
cd MyGame

git init
# Copy .gitignore from §2
# Copy .gitattributes from §3
git add -A
git commit -m "init: MonoGame project scaffold"

gh repo create MyGame --private --source=. --push
```

### Useful Aliases

Add to `~/.gitconfig`:

```ini
[alias]
    s = status --short
    l = log --oneline -20
    lg = log --oneline --graph --all -30
    d = diff
    ds = diff --staged
    aa = add -A
    cm = commit -m
    pu = push
    pl = pull
    co = checkout
    br = branch
    last = log -1 HEAD --stat
    undo = reset --soft HEAD~1
    wip = "!git add -A && git commit -m 'WIP [skip ci]'"
```

---

## TL;DR

1. **Use Git.** No excuses.
2. **Set up `.gitignore` and `.gitattributes` before your first real commit.**
3. **LFS for binary assets** — images, audio, fonts, Aseprite files.
4. **Commit working states frequently** with descriptive messages.
5. **Stay on `main`** unless you're doing something risky.
6. **Tag milestones** so you can always find playable builds.
7. **Push daily** to at least one remote.
8. **Never track build output** (`bin/`, `obj/`, `Content/bin/`).
9. **Test a clean clone** periodically to catch missing files.
10. **Learn `bisect`, `stash`, and `reflog`** — they'll save you.

Version control isn't overhead. It's the foundation that lets you take risks, track
progress, and sleep at night knowing your game is safe.
