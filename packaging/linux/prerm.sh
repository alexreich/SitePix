#!/bin/sh
# Pre-remove hook for .deb / .rpm. Stop any package-installed system timer
# (none today — reserved for future use). User-level systemd timers / cron
# entries were created per-user by sitepix-install-schedule and should be
# torn down by the user with `sitepix-install-schedule --uninstall`.
set -e
exit 0
