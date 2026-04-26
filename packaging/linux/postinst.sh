#!/bin/sh
# Post-install hook for the sitepix .deb / .rpm packages.
# Keep this minimal and idempotent — postinst runs as root; we don't want to
# start user-scope services from here or reach into $HOME.
set -e

# Refresh icon + desktop caches if the tools are around. Both are optional —
# failure is not a reason to fail the whole install.
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database -q /usr/share/applications >/dev/null 2>&1 || true
fi

cat <<'EOF'

SitePix is installed with the bundled kadampa.org profile.

Run once to populate your photos folder, then set up daily syncs:
  sitepix
  sitepix-install-schedule       # installs systemd user timer or cron job

Photos download to ~/Pictures/SitePix by default. To use a different source
site, pass a profile JSON on the command line:
  sitepix /opt/sitepix/samples/petapixel.com.json

See README.md for the full profile schema.

EOF

exit 0
