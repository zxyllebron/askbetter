#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
uuid='askbetter@zxyllebron.github.io'
if [ "$(id -u)" -eq 0 ]; then
    echo 'Run this installer as your desktop user, not root.' >&2; exit 1
fi
# The repository ZIP and the release archive use the same installer.
if [ ! -d "$root/extension" ]; then
    temporary=$(mktemp -d)
    trap 'rm -rf -- "$temporary"' EXIT HUP INT TERM
    /usr/bin/python3 "$root/../scripts/package_linux.py" --output "$temporary" >/dev/null
    tar -xzf "$temporary/AskBetter-Ubuntu-24.04-GNOME.tar.gz" -C "$temporary"
    "$temporary/AskBetter-Ubuntu-24.04/install.sh" "$@"
    exit $?
fi
major=$(gnome-shell --version | sed -n 's/.* \([0-9][0-9]*\)\..*/\1/p')
if [ "$major" != '46' ]; then
    echo 'This package targets GNOME 46 on Ubuntu 24.04.' >&2; exit 1
fi
if ! /usr/bin/python3 -c 'import gi, pyatspi, dbus; gi.require_version("Gtk", "3.0")' >/dev/null 2>&1; then
    echo 'Installing Ubuntu dependencies; your system may request authentication.'
    if [ -t 0 ]; then
        sudo apt-get install -y python3 python3-gi python3-pyatspi python3-dbus gir1.2-gtk-3.0 gnome-shell-extension-prefs libglib2.0-bin
    else
        pkexec /usr/bin/apt-get install -y python3 python3-gi python3-pyatspi python3-dbus gir1.2-gtk-3.0 gnome-shell-extension-prefs libglib2.0-bin
    fi
fi
destination="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$uuid"
gnome-extensions disable "$uuid" 2>/dev/null || true
mkdir -p "$destination"
cp -R "$root/extension/." "$destination/"
glib-compile-schemas "$destination/schemas"
gsettings set org.gnome.desktop.interface toolkit-accessibility true
/usr/bin/python3 - "$uuid" "$destination" <<'PY'
import sys
from pathlib import Path
from gi.repository import Gio
settings=Gio.Settings.new('org.gnome.shell')
enabled=settings.get_strv('enabled-extensions')
if sys.argv[1] not in enabled:
    settings.set_strv('enabled-extensions',enabled+[sys.argv[1]])
Gio.Settings.sync()
# Always use the installed location, even if the extracted archive is moved.
path=Path(sys.argv[2])/'linux/service.py'
appdir=Path(__import__('os').environ.get('XDG_DATA_HOME',Path.home()/'.local/share'))/'applications'
appdir.mkdir(parents=True,exist_ok=True)
escaped=str(path).replace('\\','\\\\').replace('"','\\"').replace('`','\\`').replace('$','\\$').replace('%','%%')
(appdir/'askbetter.desktop').write_text('[Desktop Entry]\nType=Application\nName=AskBetter\nComment=Optimize Codex prompt drafts\nExec=/usr/bin/python3 "'+escaped+'" --standalone\nIcon='+str(Path(sys.argv[2])/'wand.svg')+'\nTerminal=false\nCategories=Development;\nStartupNotify=true\n',encoding='utf-8')
PY
if gnome-extensions enable "$uuid" 2>/dev/null; then
    echo 'AskBetter enabled. Return to Codex and click its circular button.'
else
    echo 'Installed. Log out and back in once to load the circular-button extension.'
    echo 'The standalone editor is available immediately from the app menu: AskBetter.'
    if [ "${1:-}" != '--no-launch' ]; then
        /usr/bin/python3 "$destination/linux/service.py" --first-run >/dev/null 2>&1 &
    fi
fi
echo 'Codex CLI is detected automatically; an existing Codex login is required.'
