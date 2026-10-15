Window lifecycle fixes for the macOS and Ubuntu preview ports, including all existing Ubuntu first-run, rich-text, compact-chat, same-window chat-switch, and Question-card fixes.

- macOS observes minimize, application hide/exit, and window destruction. Minimized windows cannot reuse cached editor controls; delayed scans cannot show a closed overlay.
- Ubuntu hides the circle, tooltip and menu when the window closes, is hidden/minimized, or loses focus, even with a pending backend poll. Existing Ubuntu installer and editor-layout improvements are retained.
- Windows contains the v1.0.1 cleanup and background-write fixes.

| Platform | Download |
| --- | --- |
| Windows | [Windows ZIP](https://github.com/zxyllebron/askbetter/releases/download/v1.0.1/AskBetter-Windows.zip) |
| Mac Apple Silicon | [Mac arm64 ZIP](https://github.com/zxyllebron/askbetter/releases/download/v1.1.0-preview.5/AskBetter-macOS-arm64.zip) |
| Mac Intel | [Mac Intel ZIP](https://github.com/zxyllebron/askbetter/releases/download/v1.1.0-preview.5/AskBetter-macOS-x86_64.zip) |
| Ubuntu 24.04 GNOME 46 | [Ubuntu X11/Wayland package](https://github.com/zxyllebron/askbetter/releases/download/v1.1.0-preview.5/AskBetter-Ubuntu-24.04-GNOME.tar.gz) |

Mac: quit AskBetter, replace the app, and reopen it. Ubuntu: rerun the installer and log out/back in once to load the updated extension and helper. Existing settings are retained.

Model and editing instructions are unchanged. Mac and Ubuntu remain preview ports: native fixtures and simulated editor tests do not establish compatibility with every signed-in Codex client. See the [platform verification guide](https://github.com/zxyllebron/askbetter/blob/main/docs/platform-testing.md) and the bundled test reports. Mac builds are not Apple-notarized; Ubuntu targets GNOME 46.
