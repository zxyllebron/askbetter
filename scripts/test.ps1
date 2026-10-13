param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testOutput = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot 'artifacts\tests' } else { [System.IO.Path]::GetFullPath($OutputDirectory) }
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$runtimeRoot = Join-Path $repoRoot 'askbetter'
foreach ($name in @('optimizer-prompt.txt','optimizer-prompt-brief.txt','optimizer-prompt-detailed.txt')) {
    Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $testOutput -Force
}
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found. Run on 64-bit Windows with .NET Framework 4.8.' }
$baseRefs = @('/reference:System.Core.dll','/reference:System.Web.Extensions.dll')
& $compiler /nologo /target:exe ('/out:' + (Join-Path $testOutput 'FakeCodex.exe')) @baseRefs (Join-Path $repoRoot 'tests\FakeCodex.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fake server compilation failed.' }
& $compiler /nologo /target:exe ('/out:' + (Join-Path $testOutput 'BackendTests.exe')) @baseRefs (Join-Path $repoRoot 'tests\BackendTests.cs') (Join-Path $runtimeRoot 'source\Backend.cs')
if ($LASTEXITCODE -ne 0) { throw 'Backend test compilation failed.' }
& (Join-Path $testOutput 'BackendTests.exe') (Join-Path $testOutput 'FakeCodex.exe')
if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
$wpfRefs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('Backend.cs','Desktop.cs','Desktop.Options.cs','Desktop.Layout.cs','LayoutGate.cs') | ForEach-Object { Join-Path $runtimeRoot ('source\' + $_) }
& $compiler /nologo /target:exe /main:FeatureTests ('/out:' + (Join-Path $testOutput 'FeatureTests.exe')) @wpfRefs (Join-Path $repoRoot 'tests\FeatureTests.cs') @sources
if ($LASTEXITCODE -ne 0) { throw 'Feature test compilation failed.' }
& (Join-Path $testOutput 'FeatureTests.exe') (Join-Path $testOutput 'FakeCodex.exe')
if ($LASTEXITCODE -ne 0) { throw 'Feature tests failed.' }
& $compiler /nologo /target:winexe ('/out:' + (Join-Path $testOutput 'AskBetter.exe')) @wpfRefs @sources
if ($LASTEXITCODE -ne 0) { throw 'First-run test compilation failed.' }
$firstRunRoot = Join-Path $testOutput ('first-run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $firstRunRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $testOutput 'AskBetter.exe') -Destination $firstRunRoot
$firstRunSettings = @{ model='gpt-6-luna'; effort='low'; serviceTier='fast'; codexPath=(Join-Path $testOutput 'FakeCodex.exe'); lengthStyle='moderate'; uiMode='initial'; timeoutSeconds=3 }
[System.IO.File]::WriteAllText((Join-Path $firstRunRoot 'settings.example.json'),($firstRunSettings | ConvertTo-Json),[System.Text.UTF8Encoding]::new($false))
$draftFile = Join-Path $firstRunRoot 'draft.txt'
$resultFile = Join-Path $firstRunRoot 'result.json'
[System.IO.File]::WriteAllText($draftFile,'First-run test: 保留关键要求。',[System.Text.UTF8Encoding]::new($false))
$arguments = @('--optimize-file',('"' + $draftFile + '"'),('"' + $resultFile + '"'))
$firstRun = Start-Process -FilePath (Join-Path $firstRunRoot 'AskBetter.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($firstRun.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $firstRunRoot 'settings.json'))) { throw 'First-run configuration was not created.' }
$firstRunResult = Get-Content -LiteralPath $resultFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$firstRunResult.ok -or $firstRunResult.prompt -ne '优化：First-run test: 保留关键要求。') { throw 'First-run request/result failed.' }
Write-Host 'First-run configuration and fallback instructions passed.'
Write-Host 'All offline tests passed. No account or model API calls were used.'
