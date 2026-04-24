#!/usr/bin/env bash
# install.sh — bootstrap SitePix on macOS from a clean machine.
#
# What it does:
#   1. Installs the .NET 10 SDK into $HOME/.dotnet via Microsoft's official
#      dotnet-install.sh (no sudo required).
#   2. Publishes a self-contained native macOS binary into dist/macos/.
#   3. Installs the Microsoft.Playwright.CLI .NET tool and uses it to
#      download Playwright's Chromium build into ~/Library/Caches/ms-playwright/.
#   4. Fixes up dist/macos/appsettings.json for macOS defaults
#      (UseMyPictures=true, Palatino font, Task Scheduler empty for
#      the first run so no LaunchAgent is created unexpectedly).
#   5. Optionally runs the binary once and opens the output folder.
#   6. Optionally enables the LaunchAgent for daily refresh at 05:30.
#
# Run from the repo root:
#   ./macos/install.sh             # install + build (no run, no LaunchAgent)
#   ./macos/install.sh --run       # also run once and open ~/Pictures/SitePix
#   ./macos/install.sh --schedule  # also enable the daily LaunchAgent at 05:30
#   ./macos/install.sh --run --schedule
#
# Env overrides:
#   DOTNET_CHANNEL=10.0   RID=osx-arm64|osx-x64   START_TIME=05:30

set -euo pipefail

DO_RUN=0
DO_SCHEDULE=0
for arg in "$@"; do
  case "$arg" in
    --run) DO_RUN=1 ;;
    --schedule) DO_SCHEDULE=1 ;;
    -h|--help)
      sed -n '1,30p' "$0"
      exit 0 ;;
    *) echo "Unknown flag: $arg" >&2; exit 2 ;;
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

# ── 4. appsettings.json sane macOS defaults ─────────────────────────────────
APPSETTINGS="$OUT/appsettings.json"
if [ -f "$APPSETTINGS" ]; then
  say "Adjusting $APPSETTINGS for macOS defaults"
  /usr/bin/sed -i '' \
    -e 's|"UseMyPictures": false|"UseMyPictures": true|' \
    -e 's|"Font": "Palatino Linotype"|"Font": "Palatino"|' \
    "$APPSETTINGS"
fi

# ── 5. First run (optional) ─────────────────────────────────────────────────
if [ "$DO_RUN" -eq 1 ]; then
  say "Running SitePix once"
  (cd "$OUT" && ./SitePix)
  DEST="$HOME/Pictures/SitePix"
  if [ -d "$DEST" ]; then
    say "Opening $DEST"
    open "$DEST"
  fi
fi

# ── 6. LaunchAgent (optional) ───────────────────────────────────────────────
if [ "$DO_SCHEDULE" -eq 1 ]; then
  say "Enabling daily LaunchAgent at $START_TIME"
  # Ensure StartTime is non-empty so TaskRegistration.RegisterMacOS fires.
  /usr/bin/sed -i '' \
    -e "s|\"StartTime\": \"\"|\"StartTime\": \"$START_TIME\"|" \
    "$APPSETTINGS"
  # Run once to let TaskRegistration write ~/Library/LaunchAgents/.
  (cd "$OUT" && ./SitePix) >/dev/null || true
  PLIST="$HOME/Library/LaunchAgents/com.kadampa.screensaver.plist"
  if [ -f "$PLIST" ]; then
    launchctl unload "$PLIST" 2>/dev/null || true
    launchctl load   "$PLIST"
    say "LaunchAgent loaded:"
    launchctl list | grep kadampa || true
  else
    echo "WARN: expected LaunchAgent plist at $PLIST but it was not created" >&2
  fi
fi

say "Done."
echo "Binary:  $REPO_ROOT/$OUT/SitePix"
echo "Config:  $REPO_ROOT/$APPSETTINGS"
echo "Images:  \$HOME/Pictures/SitePix"
echo
echo "Next:  System Settings → Screen Saver → Classic → a slideshow module"
echo "       → Options → Choose Folder… → ~/Pictures/SitePix"
echo "       (See macos/PRD.md §6.6 for details.)"
