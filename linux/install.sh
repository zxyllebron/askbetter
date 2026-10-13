#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
uuid='askbetter@zxyllebron.github.io'
if [ "$(id -u)" -eq 0 ]; then
    echo 'Run this installer as your desktop user, not root.' >&2; exit 1
fi
if [ "${1:-}" = '--install-deps' ]; then
    sudo apt-get update
    sudo apt-get install -y python3 python3-gi python3-pyatspi python3-dbus gir1.2-gtk-3.0 gnome-shell-extension-prefs libglib2.0-bin
fi
if ! /usr/bin/python3 -c 'import gi, pyatspi, dbus; gi.require_version("Gtk", "3.0")' >/dev/null 2>&1; then
    echo 'Missing Ubuntu dependencies. Run: ./install.sh --install-deps' >&2; exit 1
fi
major=$(gnome-shell --version | sed -n 's/.* \([0-9][0-9]*\)\..*/\1/p')
if [ "$major" != '46' ]; then
    echo 'This package targets GNOME 46 on Ubuntu 24.04.' >&2; exit 1
fi
destination="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$uuid"
mkdir -p "$destination"
cp -R "$root/extension/." "$destination/"
glib-compile-schemas "$destination/schemas"
# Register without replacing any of the user's other enabled extensions.
/usr/bin/python3 - "$uuid" <<'PY'
import sys
from gi.repository import Gio
settings=Gio.Settings.new('org.gnome.shell')
enabled=settings.get_strv('enabled-extensions')
if sys.argv[1] not in enabled:
    settings.set_strv('enabled-extensions',enabled+[sys.argv[1]])
Gio.Settings.sync()
PY
if gnome-extensions enable "$uuid" 2>/dev/null; then
    echo 'AskBetter enabled. Return to Codex and click its circular button.'
else
    echo 'Installed. Log out and back in once so GNOME can load the new extension.'
fi
echo 'Both X11 and Wayland GNOME sessions use this same package.'
echo 'Codex CLI must be installed and signed in; settings: ~/.config/askbetter/settings.json'
