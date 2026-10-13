#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/../.."
repo="$PWD"
session="${1:?x11 or wayland required}"
if [[ -z "${ASKBETTER_TEST_RUNTIME:-}" ]]; then
    export ASKBETTER_TEST_RUNTIME="$(mktemp -d)"
fi
export XDG_RUNTIME_DIR="$ASKBETTER_TEST_RUNTIME"
chmod 700 "$XDG_RUNTIME_DIR"
export XDG_CONFIG_HOME="$XDG_RUNTIME_DIR/config"
export XDG_DATA_HOME="$XDG_RUNTIME_DIR/data"
export XDG_SESSION_TYPE="$session"
export NO_AT_BRIDGE=0
export GTK_MODULES=gail:atk-bridge
export PYTHONPATH="$repo${PYTHONPATH:+:$PYTHONPATH}"
if [[ "${ASKBETTER_SESSION_BUS:-}" != '1' ]]; then
    export ASKBETTER_SESSION_BUS=1
    # DConf and other D-Bus-activated services must inherit these same XDG paths
    # and DISPLAY. Starting the bus earlier makes settings appear to reset.
    exec dbus-run-session -- bash "$repo/tests/linux/run_session.sh" "$session"
fi
mkdir -p "$XDG_CONFIG_HOME/askbetter" "$XDG_DATA_HOME/gnome-shell/extensions"
python3 scripts/package_linux.py
tar -xzf artifacts/linux/AskBetter-Ubuntu-24.04-GNOME.tar.gz -C "$XDG_RUNTIME_DIR"
cp -R "$XDG_RUNTIME_DIR/AskBetter-Ubuntu-24.04/extension" "$XDG_DATA_HOME/gnome-shell/extensions/askbetter@zxyllebron.github.io"
cp -R tests/linux/driver-extension "$XDG_DATA_HOME/gnome-shell/extensions/askbetter-tests@local"
glib-compile-schemas "$XDG_DATA_HOME/gnome-shell/extensions/askbetter@zxyllebron.github.io/schemas"
cat > "$XDG_RUNTIME_DIR/fake-codex" <<EOF
#!/bin/sh
exec /usr/bin/python3 "$repo/tests/portable/fake_codex.py" --delay 1.2
EOF
chmod +x "$XDG_RUNTIME_DIR/fake-codex"
python3 - "$XDG_RUNTIME_DIR" <<'PY'
import json,sys
from pathlib import Path
root=Path(sys.argv[1])
(root/'config/askbetter/settings.json').write_text(json.dumps({'codexPath':str(root/'fake-codex')}))
PY
gsettings set org.gnome.desktop.interface toolkit-accessibility true
gsettings set org.gnome.shell enabled-extensions "['askbetter@zxyllebron.github.io', 'askbetter-tests@local']"
gsettings set org.gnome.shell disable-user-extensions false
gsettings get org.gnome.shell enabled-extensions
gnome-shell --version
mkdir -p artifacts/tests
if [[ "$session" == 'wayland' ]]; then
    gnome-shell --nested --wayland --wayland-display=askbetter-wayland >artifacts/tests/gnome-wayland.log 2>&1 &
    export WAYLAND_DISPLAY=askbetter-wayland
    export GDK_BACKEND=wayland
else
    gnome-shell --x11 --replace >artifacts/tests/gnome-x11.log 2>&1 &
    export GDK_BACKEND=x11
fi
shell_pid=$!
trap 'kill "$shell_pid" 2>/dev/null || true' EXIT
/usr/bin/python3 tests/linux/session_test.py
