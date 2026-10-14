# AskBetter for macOS

Requires macOS 13 or later and the Codex/ChatGPT desktop app plus an installed, signed-in Codex CLI. Download the `arm64` package for Apple Silicon or the `x86_64` package for an Intel Mac. The Python helper runtime is bundled; users do not need to install Python.

This is a preview port. The build is locally ad-hoc signed, not Apple-notarized. macOS may ask you to explicitly approve opening it; AskBetter does not disable Gatekeeper or change your system security settings. See the repository's platform test report for completed checks and remaining real-Codex validation.

## Run

1. Extract the platform ZIP and move the entire `AskBetter.app` to Applications or another stable location.
2. Open AskBetter. Allow it in **System Settings → Privacy & Security → Accessibility** when prompted. This is required to locate and replace the current Codex draft.
3. Return to Codex. Click the small circular wand button to the left of the model selector. The result replaces the draft without sending it.

Keep the app in the same location after granting permission; changing its location or build may require macOS to authorize it again.

To update, quit AskBetter from its menu-bar menu, replace the old app with the new download, and reopen it. Closing, hiding, or minimizing Codex hides the circle without waiting for editor discovery.

## Controls

The button matches Windows: 26 × 26 logical pixels, 16 × 16 wand, and an 8-pixel gap to the model selector. It hides during zooming/resizing and returns when the layout settles. Right-click for Brief, Moderate, Detailed, Standalone dialog, and Change model. The interface currently uses the Windows Chinese labels.

Ctrl+Option+Z (also Cmd+Option+Z) undoes the last replacement. Ctrl+Option+Q (also Cmd+Option+Q), or Quit in the menu-bar menu, exits AskBetter. The original Codex input box is optimized only by clicking the circular button.

In the standalone dialog, click Optimize or press Enter; Shift+Enter inserts a new line. Marked IME text is handled by the text view rather than submitted for optimization. Settings and the text-area context menus have the same five choices, with Return to circular-button mode as item 4. Standalone output is selectable for copying and is not automatically inserted into Codex.

## Configuration

Defaults: GPT-6 Luna / Low / Fast / Moderate. Local settings are in `~/Library/Application Support/AskBetter/settings.json`. If the CLI is not found, set `codexPath` to its absolute executable path and restart AskBetter. The helper checks PATH, common Homebrew/local binary directories, and common desktop bundle locations.

The three editable prompt files are in `AskBetter.app/Contents/Resources/prompts/`. Editing a signed app's resources changes its signature; for custom distributions, rebuild/sign the bundle using the supplied build script. The optimization model does not change your main chat model.

The original draft, window, and document are checked before replacement. Changed drafts are preserved; errors/results can be viewed in the standalone dialog. Direct Accessibility writes are preferred; verified Cmd+A/Cmd+V is used only if the editor is readable but its text value cannot be set. The clipboard is restored if nobody changes it during replacement. Return is never sent. Attachment preservation and cursor behavior need target-client verification.

## Build

From the full source repository on the matching Mac architecture:

```sh
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install 'pyinstaller==6.16.0'
bash macos/build.sh
```

Outputs are in `artifacts/macos/`. The build uses Swift/AppKit and a bundled Python helper. Apple Developer signing/notarization credentials are not included in this project.

Bundled Python, OpenSSL, and the PyInstaller bootloader retain their upstream licenses; the ZIP includes these in `LICENSES/`.
