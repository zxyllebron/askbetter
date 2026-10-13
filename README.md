# AskBetter

English | [简体中文](README.zh-CN.md)

A community-built prompt editor for the Codex desktop app on Windows. Click the small circular button beside the model selector to organize and refine your draft, then review the result in the same input box before sending.

Includes a portable executable, editable optimization prompts, C# source code, and a reusable Agent Skill.

## Features

- Three editing modes: **Brief, Moderate, and Detailed**. Rewrites the draft without executing its task.
- Automatically detects the current input box and follows chat changes.
- Replaces your draft without sending it. Supports undoing the last replacement.
- A 26 × 26 logical-pixel circular button with a wand icon, positioned to the left of the model selector.
- Hides during zooming or resizing and reappears when the layout settles. Pressing Ctrl alone does not hide it.
- Standalone dialog with Enter to optimize and Shift+Enter for a new line.
- Configurable optimization model, using your local Codex login without a separate API key.

The desktop interface and bundled optimization instructions currently use Chinese. You can customize the instruction files.

## Quick start

[Download the Windows package](https://github.com/zxyllebron/askbetter/releases/latest/download/AskBetter-Windows.zip) · [All releases](https://github.com/zxyllebron/askbetter/releases)

### Requirements

Windows 10/11 (64-bit), .NET Framework 4.8, the Codex desktop app, and an installed, signed-in Codex CLI. An internet connection and access to the selected model, Low reasoning, and Fast mode are required.

The button uses Windows UI Automation and requires a recognizable local Codex/ChatGPT desktop input box. It does not support browser, IDE, macOS, or Linux interfaces. The Agent Skill can be used separately in compatible clients.

### Run

1. Download and extract the Windows package or repository ZIP into a writable folder.
2. Double-click `askbetter/AskBetter.exe`.
3. On first launch, the app creates `settings.json` from `settings.example.json`. Defaults: **GPT-6 Luna / Low / Fast / Moderate / circular-button mode**.
4. Return to Codex. AskBetter automatically detects the input box. Write a draft and click the circular button beside the model selector. The result replaces your draft without sending it. Switching chats requires no additional setup.

AskBetter checks PATH for the CLI, then looks for the local Codex desktop CLI installation. If the CLI cannot be found, set `codexPath` in `askbetter/settings.json` to your `codex.exe` absolute path and restart. Check the CLI with `codex --version` and `codex login status`. AskBetter does not perform login for you.

Keep the entire `askbetter` folder together when moving it.

## Menu and shortcuts

Right-click the button to open five options:

| Menu label | Meaning |
| --- | --- |
| 简短 | Brief editing |
| 适中 | Moderate editing |
| 详细 | Detailed editing |
| 单独对话框模式 | Open the standalone dialog |
| 更改模型 | Change the optimization model |

The standalone dialog's **设置 (Settings)** button and right-click menu offer the same choices. Item 4 becomes **初始模式 (Return to circular-button mode)**.

Changing the editing mode only changes the instructions; it does not submit the draft or change the model. Model selection affects the assistant, not your main chat model.

| Action | Control |
| --- | --- |
| Optimize and replace the current chat draft | Click the circular button |
| Undo the last replacement | Ctrl+Alt+Z |
| Exit AskBetter | Ctrl+Alt+Q |
| Optimize in the standalone dialog | Click 优化 (Optimize) or press Enter |
| Insert a new line in the standalone dialog | Shift+Enter |

Closing the standalone dialog returns to circular-button mode. Select its output and press Ctrl+C to copy it. Standalone results are not automatically inserted into Codex. Shortcuts may not respond if another application has registered them.

## Customize prompts and settings

Edit these UTF-8 files and restart AskBetter to apply changes:

| Mode | Instruction file | Focus |
| --- | --- | --- |
| Brief | [optimizer-prompt-brief.txt](askbetter/optimizer-prompt-brief.txt) | Remove redundancy and combine similar requirements |
| Moderate | [optimizer-prompt.txt](askbetter/optimizer-prompt.txt) | Clarify the current task, final goal, and necessary conditions |
| Detailed | [optimizer-prompt-detailed.txt](askbetter/optimizer-prompt-detailed.txt) | Organize background, tasks, steps, goals, and constraints |

The model returns `{"prompt":"edited text"}`. Only the text is inserted into your draft. Detailed mode does not always produce a longer result or a list.

`settings.example.json` is the public template. `settings.json` stores local settings and is excluded by `.gitignore`. Menu selections are saved and restored on restart. Restart after editing settings manually.

Settings include `model`, `effort`, `serviceTier`, `codexPath`, `lengthStyle`, `uiMode`, `timeoutSeconds`, `buttonSize`, `anchorGap`, and control-name matching rules. The model picker retains Low/Fast. If the selected model or speed is unavailable, AskBetter reports an error and preserves the draft instead of automatically switching models or speed.

## Use as an Agent Skill

Copy the `askbetter` folder into a skill directory recognized by your client, such as `~/.agents/skills/askbetter` or a project's `.agents/skills/askbetter`. On Windows, run this from the repository root:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\askbetter\install-skill.ps1
~~~

The installer does not overwrite an existing directory. Use `-Destination` to choose another location. Invoke `$askbetter` in Codex and provide your draft and desired editing mode.

The skill returns JSON according to the selected instructions. It does not add a desktop button or change the chat model. The button app works without installing the skill.

## Build, test, and package

These commands require the full source repository. Run them from the repository root:

~~~powershell
# Build askbetter/AskBetter.exe
powershell -NoProfile -ExecutionPolicy Bypass -File .\askbetter\build.ps1

# Run offline checks
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1

# Build artifacts/releases/AskBetter-Windows.zip
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
~~~

The build uses the Windows .NET Framework C# compiler and WPF/UI Automation assemblies. It requires no NuGet, Node.js, Python, or additional .NET SDK. Exit any running executable at the output path before rebuilding. The prebuilt executable is not code-signed.

Tests use a local simulated app-server without login, network access, or real model calls. They cover request isolation, Unicode, errors, empty responses, timeout recovery, mode routing, menus, settings, Enter/IME handling, Ctrl/zoom handling, and positioning geometry. The suite includes 7 backend checks, 66 feature checks, and a first-launch configuration check.

The release package excludes local settings and account files. Keep generated files and credentials out of contributions.

## How it works and handles data

AskBetter reads only the current draft, without collecting chat history or attachments. Through the local Codex app-server, it creates an independent temporary session and sends the draft and editing instructions to the Codex service. The background process closes when you exit AskBetter.

Before replacement, it checks the input box identity, window title, and draft snapshot. If any change during optimization, it leaves your draft untouched and keeps the result in the standalone dialog. Some rich-text editors require Ctrl+A/Ctrl+V; AskBetter attempts to restore the clipboard afterward. Cursor position is not preserved.

AskBetter does not save draft logs or copy login credentials. Settings are stored locally. Draft processing consumes your Codex account allowance; local CLI records and service-side processing are outside this project's control. Passive keyboard and mouse listeners detect zooming and clicks without recording keystroke content.

Client updates may change the UI Automation controls. Attachment preservation, rich text, input methods, and multiple monitors require testing in the actual client. Review the optimized text before sending. If detection fails, use `AskBetter.exe --standalone` for a separate editing dialog.

## Repository layout

~~~text
askbetter/             Portable app, settings template, editing prompts, and skill
  source/              C# source and DPI manifest
  agents/              Skill display metadata
tests/                 Offline tests and simulated app-server
scripts/               Test and release packaging scripts
README.md              English documentation
README.zh-CN.md        Chinese documentation
LICENSE
~~~

Further details are available in the [Chinese README](README.zh-CN.md) and [Chinese usage guide](askbetter/使用说明.md).

## License and contributions

[MIT License](LICENSE). You may use, modify, and redistribute the project while retaining the license. Issues and improvements are welcome. For UI issues, include Windows, Codex CLI, and desktop app versions with reproduction steps. Run the offline checks before contributing code.

Codex, ChatGPT, and the models are provided by OpenAI. AskBetter is an independent community tool.
