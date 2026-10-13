$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ ('/win32manifest:' + (Join-Path $PSScriptRoot 'source\app.manifest')) ('/out:' + (Join-Path $PSScriptRoot 'AskBetter.exe')) @refs (Join-Path $PSScriptRoot 'source\Backend.cs') (Join-Path $PSScriptRoot 'source\Desktop.cs') (Join-Path $PSScriptRoot 'source\Desktop.Options.cs') (Join-Path $PSScriptRoot 'source\Desktop.Layout.cs') (Join-Path $PSScriptRoot 'source\LayoutGate.cs')
if ($LASTEXITCODE -ne 0) { throw 'AskBetter 编译失败' }
