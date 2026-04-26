#!/usr/bin/env bash
# install.sh — bootstrap SitePix on macOS from a clean machine.
#
# What it does:
#   1. Installs the .NET 10 SDK into $HOME/.dotnet via Microsoft's official
#      dotnet-install.sh (no sudo required).
#   2. Publishes a self-contained native macOS binary into dist/macos/.
#   3. Installs the Microsoft.Playwright.CLI .NET tool and uses it to
#      download Playwright's Chromium build into ~/Library/Caches/ms-playwright/.
#   4. Prompts for a source profile (numbered list, default = first) and
#      copies that profile's JSON over dist/macos/appsettings.json so the
#      app is ready to run with that source.
#   5. Optionally runs the binary once and opens the output folder.
#   6. Optionally enables a daily LaunchAgent.
#   7. Prints the exact path of dist/macos/appsettings.json so the user
#      knows where to tweak settings later.
#
# Run from the repo root:
#   ./macos/install.sh                         # interactive picker
#   ./macos/install.sh --source petapixel      # non-interactive
#   ./macos/install.sh --source kadampa --run  # also fetch + open folder
#   ./macos/install.sh --source petapixel --schedule  # also enable daily
#
# Env overrides:
#   DOTNET_CHANNEL=10.0   RID=osx-arm64|osx-x64   START_TIME=05:30

set -euo pipefail

DO_RUN=0
DO_SCHEDULE=0
SOURCE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --run) DO_RUN=1; shift ;;
    --schedule) DO_SCHEDULE=1; shift ;;
    --source) SOURCE="${2:-}"; shift 2 ;;
    --source=*) SOURCE="${1#--source=}"; shift ;;
    -h|--help)
      sed -n '1,30p' "$0"
      exit 0 ;;
    *) echo "Unknown flag: $1" >&2; exit 2 ;;
  esac
done

DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
START_TIME="${START_TIME:-05:30}"
RID="${RID:-}"
if [ -z "$RID" ]; then
  case "$(uname -m)" in
    arm64)  RID="osx-arm64" ;;
    x86_64) RID="osx-x64"   ;;
    *) echo "Unsupported arch: $(uname -m)" >&2; exit 1 ;;
  esac
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

CSPROJ="SitePix/SitePix.csproj"
OUT="dist/macos"
DOTNET_DIR="$HOME/.dotnet"

say() { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }

# Source catalog — most popular at top. Each entry: id|description.
# Order matches the numbered menu shown to the user.
SOURCES=(
  "petapixel|Photography news, ~1.5M monthly readers (verified)"
  "atlasobscura|Travel curiosities & long-form photo essays"
  "fstoppers|Photography community: news, originals, education"
  "thephoblographer|Photo gear reviews & sample galleries"
  "smashingmagazine|Web design & code, screenshot-heavy"
  "kadampa|Buddhist news from kadampa.org (verified, original profile)"
)

# ── 1. .NET SDK ─────────────────────────────────────────────────────────────
if ! command -v dotnet >/dev/null 2>&1 && [ ! -x "$DOTNET_DIR/dotnet" ]; then
  say "Installing .NET $DOTNET_CHANNEL SDK to $DOTNET_DIR"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR"
else
  say ".NET SDK already present — skipping install"
fi

export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$DOTNET_DIR/tools:$PATH"
dotnet --version

# ── 2. Publish ──────────────────────────────────────────────────────────────
say "Publishing self-contained binary for $RID"
dotnet publish "$CSPROJ" -c Release -r "$RID" --self-contained -o "$OUT"

# ── 3. Playwright Chromium ──────────────────────────────────────────────────
if [ ! -d "$HOME/Library/Caches/ms-playwright" ] \
   || [ -z "$(ls -A "$HOME/Library/Caches/ms-playwright" 2>/dev/null)" ]; then
  say "Installing Playwright CLI tool"
  if ! command -v playwright >/dev/null 2>&1; then
    dotnet tool install --global Microsoft.Playwright.CLI
  fi
  say "Downloading Playwright Chromium"
  (cd "$(dirname "$CSPROJ")" && playwright install chromium)
else
  say "Playwright browsers already cached — skipping install"
fi

# ── 4. Source picker ────────────────────────────────────────────────────────
APPSETTINGS="$OUT/appsettings.json"

# Validate --source if supplied.
if [ -n "$SOURCE" ]; then
  if [ ! -f "samples/${SOURCE}.json" ]; then
    echo "ERROR: --source '$SOURCE' has no matching samples/${SOURCE}.json" >&2
    echo "Available: $(ls samples/ | sed 's/\.json$//' | tr '\n' ' ')" >&2
    exit 1
  fi
fi

if [ -z "$SOURCE" ]; then
  echo
  echo "──────────────────────────────────────────────────────────────────"
  echo " Pick a source profile (most popular first):"
  echo "──────────────────────────────────────────────────────────────────"
  i=1
  for entry in "${SOURCES[@]}"; do
    id="${entry%%|*}"
    desc="${entry#*|}"
    printf "  %d) %-18s — %s\n" "$i" "$id" "$desc"
    i=$((i+1))
  done
  echo "  0) Skip — leave the bundled appsettings.json as-is"
  echo
  read -r -p "Enter number [1]: " choice
  choice="${choice:-1}"

  if [ "$choice" = "0" ]; then
    say "Skipping picker — bundled appsettings.json (kadampa default) kept"
    SOURCE=""
  elif [ "$choice" -ge 1 ] 2>/dev/null && [ "$choice" -le "${#SOURCES[@]}" ]; then
    SOURCE="${SOURCES[$((choice-1))]%%|*}"
  else
    echo "Invalid choice; defaulting to 1 (${SOURCES[0]%%|*})"
    SOURCE="${SOURCES[0]%%|*}"
  fi
fi

if [ -n "$SOURCE" ]; then
  say "Selected source: $SOURCE"
  cp "samples/${SOURCE}.json" "$APPSETTINGS"
fi

# ── 5. First run (optional) ─────────────────────────────────────────────────
if [ "$DO_RUN" -eq 1 ]; then
  say "Running SitePix once"
  (cd "$OUT" && ./SitePix)
  # Read the chosen SubDirectory + UseMyPictures back from the active config.
  DEST=$(python3 -c "
import json, os, sys, re
p = sys.argv[1]
text = open(p).read()
text = re.sub(r'^\s*//.*$', '', text, flags=re.M)   # strip line comments
cfg = json.loads(text)
sub = cfg.get('Directories', {}).get('SubDirectory', 'SitePix')
ump = cfg.get('Directories', {}).get('UseMyPictures', False)
base = cfg.get('Directories', {}).get('Base', '') or ''
if ump:
    print(os.path.join(os.path.expanduser('~/Pictures'), sub))
else:
    print(os.path.join(base, sub))
" "$APPSETTINGS")
  if [ -d "$DEST" ]; then
    say "Opening $DEST"
    open "$DEST"
  fi
fi

# ── 6. LaunchAgent (optional) ───────────────────────────────────────────────
if [ "$DO_SCHEDULE" -eq 1 ]; then
  say "Enabling daily LaunchAgent at $START_TIME"
  /usr/bin/sed -i '' \
    -e "s|\"StartTime\": \"\"|\"StartTime\": \"$START_TIME\"|" \
    "$APPSETTINGS"
  (cd "$OUT" && ./SitePix) >/dev/null || true
  PLIST_GLOB=$(ls "$HOME/Library/LaunchAgents"/com.sitepix.*.plist 2>/dev/null | head -1 || true)
  if [ -n "$PLIST_GLOB" ]; then
    launchctl unload "$PLIST_GLOB" 2>/dev/null || true
    launchctl load   "$PLIST_GLOB"
    say "LaunchAgent loaded:"
    launchctl list | grep sitepix || true
  else
    echo "WARN: expected a com.sitepix.*.plist in ~/Library/LaunchAgents but none was created" >&2
  fi
fi

# ── 7. Final summary ────────────────────────────────────────────────────────
ABS_APPSETTINGS="$REPO_ROOT/$APPSETTINGS"
ABS_BIN="$REPO_ROOT/$OUT/SitePix"

echo
echo "──────────────────────────────────────────────────────────────────"
echo " ✓ SitePix is installed."
echo "──────────────────────────────────────────────────────────────────"
echo
echo " Binary       : $ABS_BIN"
echo " Settings file: $ABS_APPSETTINGS"
echo " Source       : ${SOURCE:-bundled default (kadampa)}"
echo
echo " Edit \"$ABS_APPSETTINGS\""
echo " any time to change source URL, image min-width, retention,"
echo " text overlay, brand colors, schedule, etc. The file has comments"
echo " explaining every option."
echo
echo " Re-run anytime:"
echo "     $ABS_BIN"
echo
echo " Or pick a different profile without editing:"
echo "     $ABS_BIN $REPO_ROOT/samples/<profile>.json"
echo
echo " For screen-saver setup (System Settings → Screen Saver → Classic"
echo " slideshow → Choose Folder…), see macos/PRD.md §6.6."
echo
