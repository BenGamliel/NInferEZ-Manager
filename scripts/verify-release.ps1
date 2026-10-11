param([Parameter(Mandatory=$true)][string]$ReleaseRoot)
$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseRoot).Path
$portable = Join-Path $release 'Portable\NInferEZ Manager'
$payload = Join-Path $release 'Payload'
$upload = Join-Path $release 'Upload'
$zipPath = Join-Path $upload 'NInferEZ-Manager-Portable-1.0.0.zip'
$setup = Join-Path $upload 'Installer\NInferEZ-Manager-Setup-1.0.0.exe'
$sourceZip = Join-Path $upload 'NInferEZ-Manager-Source-1.0.0.zip'
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Description) {
    if (!$Condition) { throw $Description }
    $checks.Add($Description)
}

$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name })
    $files = @(Get-ChildItem -LiteralPath $portable -File -Recurse)
    Check ($entries.Count -eq $files.Count) 'Portable archive contains exactly the released Portable files'
    foreach ($entry in $entries) {
        $name = $entry.FullName.Replace('/',[IO.Path]::DirectorySeparatorChar)
        Check (!$name.StartsWith('\') -and $name -notmatch '(^|[\\/])\.\.([\\/]|$)') 'Archive entry is a safe relative path'
        $target = Join-Path $portable $name
        Check ((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -eq $entry.Length) 'Archive file exists with the expected length'
        $stream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        Check ($actual -eq (Get-FileHash -LiteralPath $target).Hash) 'Archive file hash matches the released Portable'
    }
} finally { $archive.Dispose() }

foreach ($relative in @('NInferEZ Manager.exe','Backend\NInferManager.Backend.exe','Assets\2beng2.png','Converter\NInferEZ.Converter.Backend.exe','Converter\app\backend\planning.py')) {
    Check ((Get-FileHash -LiteralPath (Join-Path $payload $relative)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $portable $relative)).Hash) "Installer payload and Portable use identical $relative"
}
Check ((Get-Item -LiteralPath $setup).VersionInfo.ProductVersion.Trim() -eq '1.0.0') 'Installer product version is 1.0.0'
Check ((Get-Item -LiteralPath (Join-Path $portable 'NInferEZ Manager.exe')).VersionInfo.ProductVersion.Trim() -eq '1.0.0') 'Portable application product version is 1.0.0'
Check (!(Test-Path -LiteralPath (Join-Path $payload 'portable.mode')) -and (Test-Path -LiteralPath (Join-Path $portable 'portable.mode'))) 'Installed and Portable data modes stay separate'
Check ((Get-ChildItem -LiteralPath $portable -File -Recurse | Where-Object { $_.Extension -in @('.pyc','.pdb','.dmp','.ninfer','.gguf','.safetensors') }).Count -eq 0) 'Public package excludes caches, symbols, dumps and model weights'

$sourceArchive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
try {
    Check (@($sourceArchive.Entries | Where-Object { $_.FullName -match '(^|[\\/])(artifacts|bin|obj|\.git|Data|Models|Engine)([\\/]|$)' }).Count -eq 0) 'Source archive excludes builds, mutable data and engine/model payloads'
    Check (@($sourceArchive.Entries | Where-Object { $_.FullName -eq 'src/NInferManager.WinUI/MainWindow.ConversionWorkflow.cs' }).Count -eq 1) 'Source archive contains the approved conversion workflow'
} finally { $sourceArchive.Dispose() }

foreach ($line in [IO.File]::ReadAllLines((Join-Path $upload 'SHA256SUMS.txt'))) {
    $parts = $line -split '  ',2
    Check ((Get-FileHash -LiteralPath (Join-Path $upload $parts[1])).Hash.ToLowerInvariant() -eq $parts[0]) 'Public artifact checksum matches'
}
$summary = [ordered]@{productVersion='1.0.0'; passedChecks=$checks.Count; archivedFiles=$files.Count; failures=@(); gpuExecution=$false; nativeInstallerExecution=$false; evidence='Archive integrity, version, content and checksum validation; installer compiled from the verified payload.'}
New-Item -ItemType Directory -Path (Join-Path $release 'Verification') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $release 'Verification\package-integrity.json'),($summary | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$summary | ConvertTo-Json
