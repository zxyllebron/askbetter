# AskBetter for Ubuntu 24.04

Target: Ubuntu 24.04 with the default GNOME 46 desktop, in either an X11 or Wayland session. KDE and other compositors are not supported by this extension.

This is a preview port. See the repository's platform test report for verified checks and remaining real-Codex validation. Do not interpret a passing build or simulated editor test as full client compatibility.

## Install

1. Install and sign in to the Codex CLI, and install the Codex/ChatGPT desktop app.
2. Extract `AskBetter-Ubuntu-24.04-GNOME.tar.gz` into a writable folder.
3. Open a terminal in the extracted folder and run `./install.sh` (or `./Start\ AskBetter.sh`). The installer automatically installs missing Ubuntu dependencies, enables toolkit accessibility, registers the extension, and adds an **AskBetter** entry to the application menu. Ubuntu may ask for your administrator password to install dependencies. Do not run the whole installer as root.
4. GNOME cannot load a newly installed local extension into an existing Wayland session. If prompted, log out and back in once. The installer opens a standalone editor immediately while the circular button is waiting for the next session. After logging in, return to Codex; the circular button appears to the left of the model selector.

No manual CLI path or accessibility configuration is needed for the standard Codex/ChatGPT Linux desktop installation. The repository ZIP also works: run `./linux/install.sh` from its root. The same package supports both display-server sessions; you do not need to switch to X11. Existing settings and other enabled extensions are preserved.

## Use

Click the 26-pixel wand button to optimize and replace the current draft without sending it. Right-click for Brief, Moderate, Detailed, Standalone dialog, and Change model. The bundled interface uses the same Chinese labels as Windows.

The button hides while resizing or zooming, then returns after the layout settles. Pressing Ctrl alone does not hide it. Draft changes or a different window prevent replacement; the completed result can be viewed in the standalone dialog.

Ctrl+Alt+Z undoes the last replacement; Ctrl+Alt+Q stops the helper until the extension is restarted. Use the GNOME Extensions app to disable/re-enable AskBetter. In the standalone dialog, Enter optimizes and Shift+Enter inserts a new line. Its Settings menu has the same five options, with Return to circular-button mode as item 4.

## Configuration and troubleshooting

Defaults: GPT-6 Luna / Low / Fast / Moderate. Settings are in `${XDG_CONFIG_HOME:-~/.config}/askbetter/settings.json`; the installed instruction files are in `~/.local/share/gnome-shell/extensions/askbetter@zxyllebron.github.io/prompts/`. Restart the extension after manual edits. Set `codexPath` to an absolute executable path if the CLI cannot be found.

`./run.sh` or the application-menu **AskBetter** entry opens a standalone editing dialog. It can work even when the target desktop input box cannot be recognized, but it does not automatically replace the Codex draft.

Automatic replacement requires a readable AT-SPI Text interface. Chromium rich editors expose paragraphs as embedded objects; AskBetter resolves their actual text and excludes CSS placeholders before making a model request. Unreadable objects and attachments cause a clear failure and preserve the draft. EditableText writes are preferred. For Chromium editors without EditableText, the helper verifies the draft, focuses and selects its text through AT-SPI, then the GNOME extension pastes using the compositor's virtual keyboard. It never presses Enter. The clipboard's preferred text or image format is restored if its contents have not changed; secondary clipboard formats are not retained by this fallback. If the editor cannot be read or safely selected, the draft is preserved.

Positioning maps AT-SPI geometry onto GNOME's visible frame or client area, accounting for server-side decorations. This works without external applications reading Wayland's global window positions.

Toolkit accessibility is enabled by the installer. If Chromium initially exposes only empty window shells, the helper requests its renderer accessibility tree once per process and restores the previous screen-reader flag after 1.5 seconds. An existing screen reader stays enabled. Duplicate Electron window titles are resolved only when exactly one frame matches the foreground window dimensions. Desktop-client updates, multi-monitor/fractional scaling, attachments, and input methods need real-client checks. Native Wayland in the upstream desktop app is still experimental.

To uninstall, disable AskBetter in GNOME Extensions, then remove only its extension directory above. User settings are kept separately. No account files or draft logs are included or saved by AskBetter.
