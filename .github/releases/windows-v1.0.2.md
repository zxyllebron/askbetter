Fixes circular-button discovery across different Codex/ChatGPT account layouts. AskBetter is not bound to an author account and requires no account whitelist or Codex rules file.

- Recognize GPT model versions, Auto, Thinking and generic/localized selectors. Existing settings with an older model-name pattern still benefit from built-in detection.
- Locate an explicitly labelled main composer when no nearby model picker is exposed. Keep the fallback button outside editable text; exclude search, rename and reply fields.
- Keep one-click replacement, editing instructions, optimization model settings and prior lifecycle fixes unchanged.

Download `AskBetter-Windows.zip`. Quit AskBetter, replace the application files while retaining local `settings.json`, and restart. Each user uses their own signed-in Codex CLI and must have access to their selected optimization model and Fast mode. Displaying the button itself does not require CLI login.

Native UIA and cross-platform tests use separate fixtures and simulated backend responses, not signed-in accounts. They validate the covered UI variants; unfamiliar upstream client layouts may still require adaptation. Test reports and checksums are attached.
