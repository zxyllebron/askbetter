$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot 'artifacts\tests\window-lifecycle'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll','WPF\UIAutomationProvider.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('Backend.cs','Desktop.cs','Desktop.Options.cs','Desktop.Layout.cs','LayoutGate.cs') | ForEach-Object { Join-Path $repoRoot ('askbetter\source\' + $_) }
# The fixture executable name matches the production target filter; it never
# connects to a Codex account or touches an existing desktop-app window.
$exe = Join-Path $output 'Codex.exe'
$report = Join-Path $output 'windows-window-lifecycle.json'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /main:WindowLifecycleTests ('/out:'+$exe) @refs (Join-Path $repoRoot 'tests\WindowLifecycleTests.cs') @sources
if ($LASTEXITCODE -ne 0) { throw 'Window lifecycle test compilation failed.' }
& $exe $report (Join-Path $repoRoot 'artifacts\tests\FakeCodex.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native window lifecycle checks failed.' }
