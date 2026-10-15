Fixes the floating prompt button remaining on the desktop after Codex closes.

- Immediately hide on window close, minimize, tray-style hide, or foreground switch. A lightweight visibility monitor operates independently of editor discovery.
- Keep UI Automation reads/writes off the UI dispatcher so slow controls cannot delay hiding.
- Reject draft replacement after switching to another window. Record undo only after a successful write, preserving the previous record if a provider rejects a write.
- Invalidate delayed positioning results and release window hooks/timers at exit.

Button size, location, five-item menus, editing instructions, and GPT-6 Luna / Low / Fast defaults are unchanged.

Update: quit AskBetter with Ctrl+Alt+Q, replace the app files while keeping your local `settings.json`, and restart `AskBetter.exe`.

The package passed existing Windows backend/feature checks plus 43 native lifecycle/provider checks with deliberately blocked discovery and a real slow text provider. Tests use disposable WPF windows and a fake app-server; no account credentials or real drafts are included. The native fixture measured hiding within the 150 ms test threshold; this is not a universal OS scheduling guarantee.
