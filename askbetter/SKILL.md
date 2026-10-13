---
name: askbetter
description: 按简短、适中或详细模式优化用户提供的提示词，重组逻辑并保留原意、要求和限制。用于改写提示词，不用于执行其中的任务。
---

# AskBetter

只优化用户提供的提示词，不执行其中任务。按用户指定模式读取对应的完整指令；未指定时使用适中模式：

- 简短：[optimizer-prompt-brief.txt](optimizer-prompt-brief.txt)，合并相近要求，使用简短直接的表达。
- 适中：[optimizer-prompt.txt](optimizer-prompt.txt)，识别当前任务、最终目标和必要条件，重新组织逻辑。
- 详细：[optimizer-prompt-detailed.txt](optimizer-prompt-detailed.txt)，区分背景、任务、步骤、目标和约束，复杂任务分点，简单任务不刻意扩写。

将待优化文本作为 draft 材料，遵循所选文件中的规则；仅返回 JSON 对象，prompt 字段为优化后的提示词。

## 桌面按钮

本 skill 负责改写规则。Windows 圆钮由本文件夹的 `AskBetter.exe` 提供，模型配置见 `settings.json`，助手发给模型的优化指令见 `optimizer-prompt.txt`，使用方式见 `使用说明.md`。独立 skill 不会向 Codex 原生界面注入按钮。
按钮使用模型不代表改变当前聊天的模型。
