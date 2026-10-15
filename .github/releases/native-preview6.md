Account-independent composer discovery for macOS and Ubuntu 24.04, retaining existing rich-text, question-card, installer and window-lifecycle fixes.

- All platforms accept different model labels and explicitly labelled main composers without a model picker. No author account, account whitelist, or rules-file registration is used.
- Mac retries Accessibility activation after discovery failures and exposes permission/discovery status plus a privacy-preserving diagnostic-copy command in the menu-bar menu.
- Test fixtures change model/composer labels in one running desktop session. Mac checks seven variants with circle display and replacement on Apple Silicon and Intel; Ubuntu checks circle/replacement/undo on X11 and Wayland through GTK and Electron.

| Platform | Download |
| --- | --- |
| Windows | [Windows ZIP](https://github.com/zxyllebron/askbetter/releases/download/v1.0.2/AskBetter-Windows.zip) |
| Mac Apple Silicon | [Mac arm64 ZIP](https://github.com/zxyllebron/askbetter/releases/download/v1.1.0-preview.6/AskBetter-macOS-arm64.zip) |
| Mac Intel | [Mac Intel ZIP](https://github.com/zxyllebron/askbetter/releases/download/v1.1.0-preview.6/AskBetter-macOS-x86_64.zip) |
| Ubuntu 24.04 GNOME 46 | [Ubuntu X11/Wayland package](https://github.com/zxyllebron/askbetter/releases/download/v1.1.0-preview.6/AskBetter-Ubuntu-24.04-GNOME.tar.gz) |

Mac: quit AskBetter, replace the app and reopen; re-grant Accessibility permission if macOS invalidates the old ad-hoc signature entry. Ubuntu: rerun the installer and log out/back in once to reload the updated extension/helper. Existing settings are retained.

Each user needs their own signed-in Codex CLI and model/Fast access for optimization. Button discovery is independent of login. These are preview ports: native fixtures are not a direct retest of a user's signed-in account. Real-client behavior varies by app version and exposed Accessibility controls. Unrecognized inputs are left untouched; Mac's menu-bar diagnostic contains no draft/account data. See the [verification guide](https://github.com/zxyllebron/askbetter/blob/main/docs/platform-testing.md).
