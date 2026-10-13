param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot 'askbetter'
& (Join-Path $runtimeRoot 'build.ps1')
$releaseRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot 'artifacts\releases' } else { [System.IO.Path]::GetFullPath($OutputDirectory) }
$stageRoot = Join-Path $releaseRoot ('stage-' + [Guid]::NewGuid().ToString('N'))
$appStage = Join-Path $stageRoot 'askbetter'
New-Item -ItemType Directory -Path $appStage -Force | Out-Null
foreach ($name in @('AskBetter.exe','settings.example.json','optimizer-prompt.txt','optimizer-prompt-brief.txt','optimizer-prompt-detailed.txt','SKILL.md','agents','使用说明.md','install-skill.ps1')) {
    Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination $appStage -Recurse
}
foreach ($name in @('README.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $stageRoot
}
$zipPath = Join-Path $releaseRoot 'AskBetter-Windows.zip'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$zipStream = [System.IO.File]::Open($zipPath,[System.IO.FileMode]::Create)
$archive = [System.IO.Compression.ZipArchive]::new($zipStream,[System.IO.Compression.ZipArchiveMode]::Create,$false)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stageRoot -Recurse -File) {
        $entryName = $file.FullName.Substring($stageRoot.Length + 1).Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entryName,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $zipStream.Dispose() }
Write-Host ('Portable release: ' + $zipPath)
Write-Host 'Local settings, accounts, tests and build caches are excluded.'
