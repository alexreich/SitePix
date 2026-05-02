#!/bin/sh
# One-shot installer for SitePix on macOS and Linux.
#
# Default usage (always installs the latest release into ~/.local):
#   curl -fsSL https://github.com/alexreich/SitePix/releases/latest/download/install.sh | sh
#
# Options:
#   VERSION=1.0.1   install a specific version instead of latest
#   PREFIX=/opt/x   install location (default: ~/.local)
#   NO_SCHEDULE=1   skip launching sitepix-install-schedule after install
#
# This script downloads the matching self-contained tarball from the GitHub
# release, extracts it, and links a `sitepix` shim onto PATH. No root required.

set -eu

REPO="alexreich/SitePix"
PREFIX="${PREFIX:-$HOME/.local}"

die()  { printf '\033[31merror:\033[0m %s\n' "$*" >&2; exit 1; }
note() { printf '\033[36m==>\033[0m %s\n' "$*"; }

# --- detect platform --------------------------------------------------------
OS="$(uname -s)"
MACHINE="$(uname -m)"
case "$OS" in
  Darwin)
    PLATFORM=osx
    case "$MACHINE" in
      arm64|aarch64) ARCH=arm64 ;;
      x86_64|amd64)  ARCH=x64 ;;
      *) die "Unsupported macOS architecture: $MACHINE" ;;
    esac
    ;;
  Linux)
    PLATFORM=linux
    case "$MACHINE" in
      aarch64|arm64) ARCH=arm64 ;;
      x86_64|amd64)  ARCH=x64 ;;
      *) die "Unsupported Linux architecture: $MACHINE" ;;
    esac
    ;;
  *) die "This script supports macOS and Linux. For Windows, use winget / choco / the installer exe." ;;
esac
RID="$PLATFORM-$ARCH"

# --- resolve version --------------------------------------------------------
if [ -z "${VERSION:-}" ]; then
  note "Resolving latest version from GitHub..."
  VERSION="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
    | sed -n 's/.*"tag_name": *"v\([^"]*\)".*/\1/p' | head -n1)"
  [ -n "$VERSION" ] || die "Could not resolve latest version. Set VERSION=<x.y.z> and retry."
fi
note "Installing SitePix $VERSION ($RID) into $PREFIX"

# --- download ---------------------------------------------------------------
TARBALL="SitePix-$VERSION-$RID.tar.gz"
URL="https://github.com/$REPO/releases/download/v$VERSION/$TARBALL"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT TERM

note "Downloading $URL"
curl -fSL --progress-bar "$URL" -o "$TMP/$TARBALL" \
  || die "Download failed. Check VERSION and your network."

# --- install ---------------------------------------------------------------
APPDIR="$PREFIX/lib/sitepix"
BINDIR="$PREFIX/bin"
mkdir -p "$APPDIR" "$BINDIR"
# Clean any previous install to avoid leftover files from different versions.
find "$APPDIR" -mindepth 1 -delete 2>/dev/null || true
tar -xzf "$TMP/$TARBALL" -C "$APPDIR"
chmod 0755 "$APPDIR/SitePix"

cat > "$BINDIR/sitepix" <<SH
#!/bin/sh
exec "$APPDIR/SitePix" "\$@"
SH
chmod 0755 "$BINDIR/sitepix"

# Ship the schedule helper next to the shim so it's callable without the tarball.
SCHED_URL="https://raw.githubusercontent.com/$REPO/v$VERSION/packaging/linux/sitepix-install-schedule"
if curl -fsSL "$SCHED_URL" -o "$BINDIR/sitepix-install-schedule" 2>/dev/null; then
  chmod 0755 "$BINDIR/sitepix-install-schedule"
else
  # Harmless: user can grab the script manually from the repo if the raw fetch fails.
  note "Scheduler helper not fetched (non-fatal) — see packaging/linux/sitepix-install-schedule in the repo."
fi

# --- PATH hint -------------------------------------------------------------
case ":$PATH:" in
  *":$BINDIR:"*) ;;
  *)
    note "$BINDIR is not on your PATH. Add it to your shell rc:"
    printf '      export PATH="%s:$PATH"\n' "$BINDIR"
    ;;
esac

# --- schedule --------------------------------------------------------------
if [ "${NO_SCHEDULE:-0}" = "0" ]; then
  note "Setting up daily schedule..."
  if [ -x "$BINDIR/sitepix-install-schedule" ]; then
    "$BINDIR/sitepix-install-schedule" || note "Schedule setup returned non-zero — you can re-run it manually."
  else
    note "Scheduler helper not available; skipping."
  fi
fi

note "Done. Run it now:"
printf '      %s/sitepix\n' "$BINDIR"
