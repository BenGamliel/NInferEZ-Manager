param(
    [string]$Version = "1.0.0",
    [string]$BuildId = "final",
    [string]$EngineSource = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($root)).AvailableFreeSpace -lt 2GB) { throw 'At least 2 GiB free disk space is required for packaging.' }
function Copy-ImmutableTree([string]$Source, [string]$Destination) {
    $sourceRoot = [IO.Path]::GetFullPath($Source)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $sourceRoot -Directory -Recurse | ForEach-Object {
        New-Item -ItemType Directory -Path (Join-Path $Destination $_.FullName.Substring($sourceRoot.Length + 1)) -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | ForEach-Object {
        $relativeFile = $_.FullName.Substring($sourceRoot.Length + 1)
        $destinationFile = Join-Path $Destination $relativeFile
        if ($relativeFile -match '^(Data|Models)[\\/]') { Copy-Item -LiteralPath $_.FullName -Destination $destinationFile }
        else {
            try { New-Item -ItemType HardLink -Path $destinationFile -Target $_.FullName -ErrorAction Stop | Out-Null }
            catch { Copy-Item -LiteralPath $_.FullName -Destination $destinationFile }
        }
    }
}
if ($Version -ne '1.0.0') { throw "This release line uses product version 1.0.0. Use BuildId for iterations." }
if ($BuildId -notmatch '^(dev|rc)-\d{3}$|^final$') { throw "BuildId must be dev-NNN, rc-NNN, or final." }
$engineManifestData = $null
if (![string]::IsNullOrWhiteSpace($EngineSource)) {
    $EngineSource = [IO.Path]::GetFullPath($EngineSource)
    $engineExe = Join-Path $EngineSource "ninfer-serve.exe"
    $engineInspector = Join-Path $EngineSource "ninfer-inspect.exe"
    $engineManifest = Join-Path $EngineSource "engine-manifest.json"
    if (!(Test-Path -LiteralPath $engineExe) -or !(Test-Path -LiteralPath $engineInspector) -or !(Test-Path -LiteralPath $engineManifest)) {
        throw "EngineSource must contain ninfer-serve.exe, ninfer-inspect.exe and engine-manifest.json."
    }
    $sourceManifest = Get-Content -LiteralPath $engineManifest -Raw | ConvertFrom-Json
    $engineManifestData = [ordered]@{ engineVersion = $sourceManifest.engineVersion; cudaArchitecture = $sourceManifest.cudaArchitecture; contractVersion = $sourceManifest.contractVersion }
}

$buildRoot = Join-Path $root "artifacts\$Version\$BuildId"
$basePayload = Join-Path $buildRoot "base"
$backend = Join-Path $basePayload "Backend"
$dist = Join-Path $buildRoot "packages"
$portableDirectory = Join-Path $dist "Portable\NInferEZ Manager"
$testDirectory = Join-Path $dist "Test Portable\NInferEZ Manager"
if ((Test-Path -LiteralPath $buildRoot) -or (Test-Path -LiteralPath $dist)) {
    throw "Release $Version already exists. Use a new version or explicitly remove the old release after reviewing it."
}

New-Item -ItemType Directory -Path $basePayload,$backend,$dist | Out-Null
dotnet publish (Join-Path $root "src\NInferManager.WinUI\NInferManager.WinUI.csproj") -c Release -r win-x64 --self-contained true -o $basePayload -p:Version=$Version -p:BuildInParallel=false -m:1
if ($LASTEXITCODE -ne 0) { throw "WinUI publish failed with exit code $LASTEXITCODE." }
dotnet publish (Join-Path $root "src\NInferManager.Backend\NInferManager.Backend.csproj") -c Release -r win-x64 --self-contained true -o $backend -p:Version=$Version -p:BuildInParallel=false -m:1
if ($LASTEXITCODE -ne 0) { throw "Backend publish failed with exit code $LASTEXITCODE." }

Get-ChildItem -LiteralPath $basePayload -File -Recurse | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -like '*.staticwebassets.*.json' } | ForEach-Object {
    $resolved = [IO.Path]::GetFullPath($_.FullName)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($basePayload) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected publish cleanup path.' }
    Remove-Item -LiteralPath $resolved -Force
}
New-Item -ItemType Directory -Path (Join-Path $basePayload "Models"),(Join-Path $basePayload "Data"),(Join-Path $basePayload "Docs") | Out-Null
Copy-Item -LiteralPath (Join-Path $root "docs\MODELS.txt") -Destination (Join-Path $basePayload "Models\README.txt")
Copy-Item -LiteralPath (Join-Path $root "docs\DATA.txt") -Destination (Join-Path $basePayload "Data\README.txt")
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $basePayload "Docs\LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $root "docs\THIRD-PARTY-NOTICES.md") -Destination (Join-Path $basePayload "Docs\THIRD-PARTY-NOTICES.md")
Copy-Item -LiteralPath (Join-Path $root "docs\RELEASE-NOTES-1.0.0.md") -Destination (Join-Path $basePayload "Docs\RELEASE-NOTES.md")
if (Test-Path -LiteralPath (Join-Path $root 'docs\USER-GUIDE.md')) { Copy-Item -LiteralPath (Join-Path $root 'docs\USER-GUIDE.md') -Destination (Join-Path $basePayload 'Docs\USER-GUIDE.md') }

New-Item -ItemType Directory -Path $portableDirectory,$testDirectory | Out-Null
Copy-ImmutableTree $basePayload $portableDirectory
Copy-ImmutableTree $basePayload $testDirectory
New-Item -ItemType File -Path (Join-Path $portableDirectory "portable.mode"),(Join-Path $testDirectory "portable.mode") | Out-Null
if ($engineManifestData) {
    $testEngine = Join-Path $testDirectory "Engine"
    New-Item -ItemType Directory -Path $testEngine | Out-Null
    Copy-ImmutableTree $EngineSource $testEngine
}

& (Join-Path $root "scripts\audit-release.ps1") -Path $basePayload
& (Join-Path $root "scripts\audit-release.ps1") -Path $portableDirectory
& (Join-Path $root "scripts\audit-release.ps1") -Path $testDirectory
& (Join-Path $root 'scripts\product-check.ps1') -PackagePath $portableDirectory
& (Join-Path $root 'scripts\product-check.ps1') -PackagePath $testDirectory -LocalTest

$portableZip = Join-Path $dist "NInferEZ-Manager-Portable-$Version.zip"
Compress-Archive -Path (Join-Path $portableDirectory "*") -DestinationPath $portableZip -CompressionLevel Optimal

if (!$SkipInstaller) {
    $iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
    if (!(Test-Path -LiteralPath $iscc)) { throw "Inno Setup 6 was not found." }
    & $iscc "/DAppVersion=$Version" "/DPayloadRoot=$basePayload" "/DDistRoot=$dist" (Join-Path $root "installer\NInferEZManager.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
}

$files = Get-ChildItem -LiteralPath $dist -File -Recurse | Where-Object { $_.Name -ne 'SHA256SUMS.txt' -and $_.FullName -notmatch '[\\/]Test Portable[\\/].*[\\/](Engine|Data|Models)[\\/]' }
$lines = $files | ForEach-Object {
    $relative = $_.FullName.Substring($dist.Length).TrimStart('\','/').Replace('\','/')
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
}
[IO.File]::WriteAllLines((Join-Path $dist "SHA256SUMS.txt"), $lines, [Text.UTF8Encoding]::new($false))
$gitCommit = if (Test-Path -LiteralPath (Join-Path $root '.git')) { git -C $root rev-parse HEAD } else { 'uncommitted' }
$sourceDirectories = @('src','tests','scripts','installer','assets','feeds','docs') | ForEach-Object { Join-Path $root $_ }
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceDirectories -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) + @(Get-ChildItem -LiteralPath $root -File)
$sourceLines = $sourceFiles | Sort-Object FullName | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.FullName.Substring($root.Length + 1).Replace('\','/'))"
}
[IO.File]::WriteAllLines((Join-Path $buildRoot 'SOURCE-SHA256SUMS.txt'), $sourceLines, [Text.UTF8Encoding]::new($false))
$buildInfo = [ordered]@{
    productVersion = $Version
    buildId = $BuildId
    channel = 'local-acceptance'
    gpuInferenceTested = $false
    visualAcceptancePending = $true
    immutableFilesShared = $true
    sourceSnapshotSha256 = (Get-FileHash -LiteralPath (Join-Path $buildRoot 'SOURCE-SHA256SUMS.txt')).Hash.ToLowerInvariant()
    sdk = (& dotnet --version)
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = $gitCommit
    testEngine = $engineManifestData
}
[IO.File]::WriteAllText((Join-Path $buildRoot 'build-info.json'), ($buildInfo | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Host "Installer, clean Portable, and ready-to-test Portable are available in $dist"
