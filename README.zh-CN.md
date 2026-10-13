# AskBetter

Windows 上的 Codex 桌面提示词优化助手。点击输入框模型选择器旁的小圆钮，将草稿改写得更有条理，并直接回填，供你检查后发送。

[English](README.md) | 简体中文

## 功能

- 26 × 26 逻辑像素圆钮，使用 16 × 16 魔棒图标，位于模型选择器左侧。
- 简短、适中、详细三档独立优化指令；只改写草稿，不执行草稿里的任务。
- 回填草稿，不自动发送；支持撤销上次回填。
- 切换聊天时重新识别输入框；实际缩放时隐藏圆钮，稳定后恢复。单按 Ctrl 不隐藏。
- 单独对话框模式：Enter 优化，Shift+Enter 换行，输出可选中复制。
- 可更改优化模型；复用本机 Codex 登录，无需在项目中填写 API Key。

## 直接使用

[下载 Windows 使用包](https://github.com/zxyllebron/askbetter/releases/latest/download/AskBetter-Windows.zip) · [查看所有发布版本](https://github.com/zxyllebron/askbetter/releases)

### 环境要求

Windows 10/11 64 位、.NET Framework 4.8、Codex 桌面应用，以及已安装并登录的 Codex CLI。需要网络和所选模型、Low 推理与 Fast 的使用权限。

圆钮依赖 Windows UI Automation，只支持可识别的本机 Codex/ChatGPT 桌面输入框。网页、IDE、macOS 和 Linux 不支持此圆钮；纯 Agent Skill 可以单独使用。

### 启动

1. 下载本仓库 ZIP 并解压，或下载 Windows 发布包。将文件夹放在可写目录，例如个人应用目录，避免放进需要管理员权限的系统目录。
2. 双击 `askbetter/AskBetter.exe`。仓库附带预编译程序，也可以按下方步骤自行构建。
3. 首次启动从 `settings.example.json` 生成本地 `settings.json`，默认 **GPT-6 Luna / Low / Fast / 适中 / 圆钮模式**。
4. 切回 Codex，助手会自动识别当前输入框；输入草稿，点击模型选择器左侧的圆钮。结果替换当前草稿，不会发送。切换对话后会自动重新定位，无需额外操作。


助手先检查 PATH，再查找 Codex 桌面应用在本机的 CLI 安装目录。如果找不到 CLI，请在 `askbetter/settings.json` 中将 `codexPath` 填为本机 `codex.exe` 的绝对路径，例如 `"C:\\Apps\\Codex\\codex.exe"`，并重启助手。CLI 可用性和登录情况可通过 `codex --version`、`codex login --help`、`codex login status` 检查；程序不会代办登录。

配置和提示词均位于程序旁边，保持整个 `askbetter` 文件夹一起移动。仅复制 EXE 时会使用内置默认指令，并失去旁边文件中的自定义内容。

## 菜单与快捷键

圆钮右键有五项：**简短、适中、详细、单独对话框模式、更改模型**。单独对话框中的“设置”和右键菜单也有五项，第 4 项为 **初始模式**，用于返回圆钮。

选择长度只更改指令，不立即提交草稿，也不改变模型。选择“更改模型”后，可以从本机列表选择或输入模型标识；模型必须接受配置中的推理强度和速度。助手不会在失败时自动切换模型或降级速度。

| 操作 | 方式 |
| --- | --- |
| 原聊天优化并回填 | 点击圆钮 |
| 撤销上次回填 | Ctrl+Alt+Z |
| 退出助手 | Ctrl+Alt+Q |
| 单独对话框优化 | 点击“优化”或按 Enter |
| 单独对话框换行 | Shift+Enter |

关闭单独对话框会回到圆钮模式，退出助手需使用 Ctrl+Alt+Q。若快捷键被其他应用占用，撤销或退出可能无法响应。

## 自定义指令与配置

三套指令可直接编辑，保存为 UTF-8，重启助手后生效：

| 模式 | 指令文件 | 重点 |
| --- | --- | --- |
| 简短 | [optimizer-prompt-brief.txt](askbetter/optimizer-prompt-brief.txt) | 删除冗余、合并要求、简洁直接 |
| 适中 | [optimizer-prompt.txt](askbetter/optimizer-prompt.txt) | 区分当前任务、最终目标和必要条件 |
| 详细 | [optimizer-prompt-detailed.txt](askbetter/optimizer-prompt-detailed.txt) | 整理背景、任务、步骤、目标和约束 |

模型返回 `{"prompt":"优化后的文本"}`，助手只将文本回填，不写入 JSON 外壳。详细模式不保证每次都变长或分点；模型仍可能遗漏强调或改变表述。

`settings.example.json` 是公开的默认模板；`settings.json` 是本机实际配置，已通过 `.gitignore` 排除。菜单保存的档位、模型、模式会在重启后恢复。手动编辑实际配置后需要重启。

配置项包括 `model`、`effort`、`serviceTier`、`codexPath`、`lengthStyle`、`uiMode`、`timeoutSeconds`、`buttonSize`、`anchorGap` 和控件名称匹配规则。默认使用 Fast；账户不支持时会报告错误并保留草稿。目前模型选择界面保持 Low/Fast，不提供速度切换。

## 单独作为 Agent Skill 使用

将 `askbetter` 文件夹复制到客户端识别的技能目录，例如个人 `~/.agents/skills/askbetter` 或项目 `.agents/skills/askbetter`。Windows 上也可以在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\askbetter\install-skill.ps1
```

安装脚本不会覆盖已存在的目录，可通过 `-Destination` 指定其他目录。然后在 Codex 中调用 `$askbetter`，提供待优化文本和所需档位。纯 skill 按所选指令返回 JSON；它本身不注入按钮，也不改变聊天模型。按钮程序无需安装 skill 即可运行。

## 从源码构建

本节及下方测试、打包命令适用于完整源码仓库。Windows 发布包用于直接运行；开发时请下载完整源码仓库。

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\askbetter\build.ps1
```

使用 Windows 自带的 .NET Framework C# 编译器和 WPF/UI Automation 程序集，不依赖 NuGet、Node、Python 或额外 .NET SDK。输出为 `askbetter/AskBetter.exe`。构建前退出正在运行的同一路径程序，避免文件被占用。预编译文件未进行代码签名。

### 离线测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

测试使用本地模拟 app-server，无需登录、联网或调用真实模型。覆盖请求隔离、Unicode、失败、空结果、超时恢复、模式指令路由、菜单、配置保存、Enter/IME 判断、Ctrl 缩放判断和定位几何规则。当前有 7 项后端检查、66 项功能检查，以及首次启动自动生成配置的检查。

这些检查不能替代真实客户端的回填、中文输入法、附件和多显示器验证。桌面控件树随客户端版本变化，贡献定位修复时请说明 Windows、Codex CLI/桌面版本及重现步骤。

### 生成发布包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

脚本先构建，再生成 `artifacts/releases/AskBetter-Windows.zip`。发布包只包含程序、默认配置模板、指令、skill、安装脚本、使用说明、README 和许可证；不会复制本地实际配置或账号文件。仓库附带 EXE，GitHub Releases 也可上传此发布包供下载。

## 工作方式与数据

点击圆钮时只读取当前草稿，不收集聊天历史或附件。通过本机 Codex app-server 创建独立临时会话，把草稿及优化指令发送到 Codex 服务，并等待完整结果。后台进程会在退出助手时关闭。

回填前检查输入框身份、窗口标题及草稿快照；发生变化时不覆盖，结果留在单独对话框中。某些富文本输入框需要聚焦后通过 Ctrl+A/Ctrl+V 粘贴，并尽量恢复剪贴板；光标位置不保留，附件与富文本结构需要在目标版本验证。

助手不保存草稿日志或复制登录凭据，配置会写入本地文件。草稿仍会由 Codex 服务处理并消耗账户额度；本地 CLI 自身的记录与服务端处理规则不由本项目控制。被动键鼠监听只用于判断前台目标的缩放和点击，不记录按键内容。

更多使用说明见 [使用说明.md](askbetter/使用说明.md)。遇到定位问题，可先使用 `AskBetter.exe --standalone` 的单独对话框模式。

## 目录

```text
askbetter/             便携程序、配置模板、三档指令与 skill
  source/             C# 源码与 DPI manifest
  agents/             skill 显示信息
tests/                离线测试和模拟 app-server
scripts/              测试与发布打包脚本
README.md
LICENSE
```

## 上传 GitHub

新建一个空仓库，再在本目录运行以下命令，把地址中的 `YOURNAME` 和仓库名替换为自己的：

```bash
git init -b main
git add .
git commit -m "Initial open-source release"
git remote add origin https://github.com/YOURNAME/askbetter.git
git push -u origin main
```

没有配置 Git 时，也可以使用 GitHub 网页的 Upload files 上传本目录内容，保持子目录结构。不要上传 `artifacts` 和运行后生成的 `settings.json`；网页上传不会自动应用 `.gitignore`。

## 许可与贡献

[MIT License](LICENSE)。可使用、修改和再分发，保留许可证即可。欢迎提交问题和改进；请先运行离线测试，避免提交个人配置、凭据或真实草稿记录。Codex、ChatGPT 与相关模型由 OpenAI 提供，本项目是独立的社区工具。
