param([string]$Destination = (Join-Path $env:USERPROFILE '.agents\skills\askbetter'))
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) { throw "目标已存在，未覆盖：$Destination。请指定其他 -Destination。" }
$required = @('SKILL.md','agents','settings.example.json','optimizer-prompt.txt','optimizer-prompt-brief.txt','optimizer-prompt-detailed.txt','使用说明.md','AskBetter.exe')
foreach ($name in $required) { if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $name))) { throw "安装文件缺失：$name" } }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SKILL.md') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'agents') -Destination $Destination -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'settings.example.json') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'optimizer-prompt.txt') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'optimizer-prompt-brief.txt') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'optimizer-prompt-detailed.txt') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.md') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AskBetter.exe') -Destination $Destination
Write-Host "已安装 skill：$Destination。无需安装 skill 也可以直接启动解压目录中的 AskBetter.exe。"
