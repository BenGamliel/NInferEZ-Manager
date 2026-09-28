param(
    [string]$Version = "0.0.1",
    [string]$BuildId = "dev-001",
    [string]$EngineSource = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Version -ne '0.0.1') { throw "This development line uses product version 0.0.1. Use BuildId for iterations." }
if ($BuildId -notmatch '^(dev|rc)-\d{3}$|^final$') { throw "BuildId must be dev-NNN, rc-NNN, or final." }
$engineManifestData = $null
if (![string]::IsNullOrWhiteSpace($EngineSource)) {
    $EngineSource = [IO.Path]::GetFullPath($EngineSource)
    $engineExe = Join-Path $EngineSource "ninfer-serve.exe"
    $engineManifest = Join-Path $EngineSource "engine-manifest.json"
    if (!(Test-Path -LiteralPath $engineExe) -or !(Test-Path -LiteralPath $engineManifest)) {
        throw "EngineSource must contain ninfer-serve.exe and engine-manifest.json."
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
dotnet publish (Join-Path $root "src\NInferManager.WinUI\NInferManager.WinUI.csproj") -c Release -r win-x64 --self-contained true -o $basePayload -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "WinUI publish failed with exit code $LASTEXITCODE." }
dotnet publish (Join-Path $root "src\NInferManager.Backend\NInferManager.Backend.csproj") -c Release -r win-x64 --self-contained true -o $backend -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Backend publish failed with exit code $LASTEXITCODE." }

Get-ChildItem -LiteralPath $basePayload -Filter '*.pdb' -File -Recurse | Remove-Item -Force
Get-ChildItem -LiteralPath $backend -Filter '*.staticwebassets.*.json' -File | Remove-Item -Force
New-Item -ItemType Directory -Path (Join-Path $basePayload "Models"),(Join-Path $basePayload "Data"),(Join-Path $basePayload "Docs") | Out-Null
Copy-Item -LiteralPath (Join-Path $root "docs\MODELS.txt") -Destination (Join-Path $basePayload "Models\README.txt")
Copy-Item -LiteralPath (Join-Path $root "docs\DATA.txt") -Destination (Join-Path $basePayload "Data\README.txt")
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $basePayload "Docs\LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $root "docs\THIRD-PARTY-NOTICES.md") -Destination (Join-Path $basePayload "Docs\THIRD-PARTY-NOTICES.md")
Copy-Item -LiteralPath (Join-Path $root "docs\RELEASE-NOTES-0.0.1.md") -Destination (Join-Path $basePayload "Docs\RELEASE-NOTES.md")

New-Item -ItemType Directory -Path $portableDirectory,$testDirectory | Out-Null
Get-ChildItem -LiteralPath $basePayload | Copy-Item -Destination $portableDirectory -Recurse
Get-ChildItem -LiteralPath $basePayload | Copy-Item -Destination $testDirectory -Recurse
New-Item -ItemType File -Path (Join-Path $portableDirectory "portable.mode"),(Join-Path $testDirectory "portable.mode") | Out-Null
if ($engineManifestData) {
    $testEngine = Join-Path $testDirectory "Engine"
    New-Item -ItemType Directory -Path $testEngine | Out-Null
    Get-ChildItem -LiteralPath $EngineSource | Copy-Item -Destination $testEngine -Recurse
}

& (Join-Path $root "scripts\audit-release.ps1") -Path $basePayload
& (Join-Path $root "scripts\audit-release.ps1") -Path $portableDirectory
& (Join-Path $root "scripts\audit-release.ps1") -Path $testDirectory

$portableZip = Join-Path $dist "NInferEZ-Manager-Portable-$Version.zip"
Compress-Archive -Path (Join-Path $portableDirectory "*") -DestinationPath $portableZip -CompressionLevel Optimal

if (!$SkipInstaller) {
    $iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
    if (!(Test-Path -LiteralPath $iscc)) { throw "Inno Setup 6 was not found." }
    & $iscc "/DAppVersion=$Version" "/DPayloadRoot=$basePayload" "/DDistRoot=$dist" (Join-Path $root "installer\NInferEZManager.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
}

$files = Get-ChildItem -LiteralPath $dist -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt'
$lines = $files | ForEach-Object {
    $relative = $_.FullName.Substring($dist.Length).TrimStart('\','/').Replace('\','/')
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $relative"
}
[IO.File]::WriteAllLines((Join-Path $dist "SHA256SUMS.txt"), $lines, [Text.UTF8Encoding]::new($false))
$gitCommit = if (Test-Path -LiteralPath (Join-Path $root '.git')) { git -C $root rev-parse HEAD } else { 'uncommitted' }
$buildInfo = [ordered]@{
    productVersion = $Version
    buildId = $BuildId
    channel = if ($BuildId -eq 'final') { 'stable' } elseif ($BuildId.StartsWith('rc-')) { 'release-candidate' } else { 'development' }
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    gitCommit = $gitCommit
    testEngine = $engineManifestData
}
[IO.File]::WriteAllText((Join-Path $buildRoot 'build-info.json'), ($buildInfo | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Host "Installer, clean Portable, and ready-to-test Portable are available in $dist"
