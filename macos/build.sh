#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
repo="$PWD"
output="${1:-$repo/artifacts/macos}"
mkdir -p "$output"
arch="$(uname -m)"
if [[ "$arch" != "arm64" && "$arch" != "x86_64" ]]; then
    echo 'Unsupported macOS processor' >&2; exit 1
fi
app="$output/AskBetter.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/prompts"
cp macos/Info.plist "$app/Contents/Info.plist"
cp askbetter/optimizer-prompt*.txt "$app/Contents/Resources/prompts/"
swiftc -swift-version 5 -O -target "$arch-apple-macos13.0" -framework AppKit -framework ApplicationServices \
    macos/Geometry.swift macos/Accessibility.swift macos/BackendBridge.swift macos/main.swift \
    -o "$app/Contents/MacOS/AskBetter"
python3 -m PyInstaller --noconfirm --clean --onedir --name askbetter-core \
    --distpath "$output/python" --workpath "$output/build" --specpath "$output/build" crossplatform/core.py
cp -R "$output/python/askbetter-core/." "$app/Contents/Resources/"
chmod +x "$app/Contents/MacOS/AskBetter" "$app/Contents/Resources/askbetter-core"
# Local ad-hoc signing preserves bundle integrity; this is not Apple notarization.
codesign --force --deep --sign - "$app"
cp macos/README.md "$output/README-macOS.md"
cp LICENSE "$output/LICENSE"
ditto -c -k --keepParent "$app" "$output/AskBetter-macOS-$arch.zip"
python3 - "$output/AskBetter-macOS-$arch.zip" "$repo" <<'PY'
import sys,zipfile
from pathlib import Path
with zipfile.ZipFile(sys.argv[1],'a',compression=zipfile.ZIP_DEFLATED) as archive:
    archive.write(Path(sys.argv[2])/'macos/README.md','README-macOS.md')
    archive.write(Path(sys.argv[2])/'LICENSE','LICENSE')
PY
echo "macOS package: $output/AskBetter-macOS-$arch.zip"
