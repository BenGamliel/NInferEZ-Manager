param(
    [string]$BuildId = 'local-review-001',
    [string]$OwnerAccount = 'BENGS_PC\2beng',
    [switch]$Release,
    [switch]$SkipQaInstaller
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$line = Split-Path -Parent $project
if ($BuildId -notmatch '^(local-review|release)-\d{3}$') { throw 'Use local-review-NNN or release-NNN.' }
$bindingFile = Join-Path $project 'src\NInferManager.Backend\Assets\release-engine.json'
$binding = Get-Content -LiteralPath $bindingFile -Raw | ConvertFrom-Json
if ($binding.product -ne 'NInferEZ Manager' -or $binding.managerVersion -ne '1.1.0') { throw 'Release engine binding does not match Manager 1.1.0.' }
if ($Release -and (!$binding.approved -or $binding.packages.Count -eq 0)) { throw 'Publication blocked: first approve and connect the tested engine packages.' }
if (!$Release -and $binding.approved) { throw 'Use -Release for an approved binding; this script never changes bindings.' }
if ($Release) {
    $architectures = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in $binding.packages) {
        $uri = $null
        if ($package.engineVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or $package.contractVersion -ne 1 -or $package.platform -ne 'windows-x64' -or
            $package.cudaArchitecture -notin @('sm86','sm89','sm120a') -or !$architectures.Add($package.cudaArchitecture) -or
            $package.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $package.sizeBytes -le 0 -or $package.sizeBytes -gt 8GB -or
            [IO.Path]::GetFileName($package.fileName) -ne $package.fileName -or
            ![Uri]::TryCreate($package.url,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or
            $uri.Host -ne 'github.com' -or !$uri.AbsolutePath.StartsWith('/BenGamliel/NInferEZ-Engine/releases/download/',[StringComparison]::Ordinal)) {
            throw 'Publication blocked: the approved tested-engine pins are invalid.'
        }
    }
}
$stableArchive = Join-Path $line 'dependencies\NInferEZ-Manager-Portable-1.0.0.zip'
if ((Get-FileHash -LiteralPath $stableArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '28adfba64dd26c1e42845532177772377ab6741ab802d69e7131e13907670f7c') { throw 'The stable Converter source package failed SHA-256 verification.' }
$output = Join-Path $line "Manager $BuildId"
if (Test-Path -LiteralPath $output) { throw 'This build already exists. Use a new build ID; existing data is never overwritten.' }
$archiveName = if ($Release) { 'NInferEZ-Manager-Portable-1.1.0.zip' } else { "NInferEZ-Manager-Portable-1.1.0-$BuildId.zip" }
$archivePath = Join-Path $line $archiveName
if (Test-Path -LiteralPath $archivePath) { throw 'The output archive already exists. Inspect the previous build state before replacing any files.' }
if ($Release -and (Test-Path -LiteralPath (Join-Path $line 'Installer\NInferEZ-Manager-Setup-1.1.0.exe'))) { throw 'The output Setup already exists. Inspect the previous build state before replacing any files.' }
$statePath = Join-Path $line "Manager-$BuildId-state.json"
if (Test-Path -LiteralPath $statePath) { throw 'This build already has a state record. Preserve it and use a new build ID.' }
$buildState = [ordered]@{product='NInferEZ Manager';version='1.1.0';buildId=$BuildId;complete=$false;stage='Starting';updatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');bindingSha256=(Get-FileHash -LiteralPath $bindingFile -Algorithm SHA256).Hash.ToLowerInvariant()}
function Save-BuildStage([string]$Stage) {
    $buildState.stage=$Stage
    $buildState.updatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    [IO.File]::WriteAllText($statePath,($buildState | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
}
Save-BuildStage 'Publishing Windows UI'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$backend = Join-Path $output 'Backend'
dotnet publish (Join-Path $project 'src\NInferManager.WinUI\NInferManager.WinUI.csproj') -c Release -r win-x64 --self-contained true -o $output -p:BuildInParallel=false -m:1
if ($LASTEXITCODE -ne 0) { throw 'WinUI publish failed.' }
Save-BuildStage 'Publishing backend'
dotnet publish (Join-Path $project 'src\NInferManager.Backend\NInferManager.Backend.csproj') -c Release -r win-x64 --self-contained true -o $backend -p:BuildInParallel=false -m:1
if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed.' }
$components = Join-Path $line "Manager-$BuildId-components"
if (Test-Path -LiteralPath $components) { throw 'Component evidence already exists for this build ID.' }
New-Item -ItemType Directory -Path $components | Out-Null
$backendAssembly = Join-Path $components 'NInferManager.Backend.dll'
$contractsAssembly = Join-Path $components 'NInferManager.Contracts.dll'
Copy-Item -LiteralPath (Join-Path $project 'src\NInferManager.Backend\bin\Release\net10.0-windows10.0.19041.0\win-x64\NInferManager.Backend.dll') -Destination $backendAssembly
Copy-Item -LiteralPath (Join-Path $project 'src\NInferManager.Contracts\bin\Release\net9.0\NInferManager.Contracts.dll') -Destination $contractsAssembly
Save-BuildStage 'Repacking verified stable Converter'

# A single verified archive keeps unchanged stable Converter bytes below the
# existing 1.0 updater entry-count limit and inside its Backend replacement unit.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$runtimeArchive = Join-Path $backend 'Converter-runtime.zip'
$archive = [IO.Compression.ZipFile]::OpenRead($stableArchive)
$runtimeStream = [IO.File]::Open($runtimeArchive,[IO.FileMode]::CreateNew)
$runtimeZip = [IO.Compression.ZipArchive]::new($runtimeStream,[IO.Compression.ZipArchiveMode]::Create,$false)
$converterCount = 0
try {
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if (!$name.StartsWith('Converter/',[StringComparison]::OrdinalIgnoreCase)) { continue }
        $relative = $name.Substring('Converter/'.Length)
        if (!$relative -or $relative.EndsWith('/')) { continue }
        if ($relative.StartsWith('/') -or $relative.Contains(':') -or ($relative.Split('/') -contains '..')) { throw 'Unsafe stable Converter path.' }
        $replacement = $runtimeZip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
        $replacement.LastWriteTime = $entry.LastWriteTime
        $inputStream = $entry.Open(); $outputStream = $replacement.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
        $converterCount++
    }
} finally { $runtimeZip.Dispose(); $runtimeStream.Dispose(); $archive.Dispose() }
if ($converterCount -lt 100) { throw 'The verified stable Converter source is incomplete.' }
$runtimeInfo = [ordered]@{schemaVersion=1;converterVersion='1.0.0';bytes=(Get-Item -LiteralPath $runtimeArchive).Length;sha256=(Get-FileHash -LiteralPath $runtimeArchive -Algorithm SHA256).Hash.ToLowerInvariant();sourceArchiveSha256='28adfba64dd26c1e42845532177772377ab6741ab802d69e7131e13907670f7c'}
$runtimeInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backend 'Converter-runtime.json') -Encoding utf8
New-Item -ItemType Directory -Path (Join-Path $output 'Docs'),(Join-Path $output 'Data'),(Join-Path $output 'Models') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination (Join-Path $output 'Docs\LICENSE.txt')
foreach ($file in @('THIRD-PARTY-NOTICES.md','USER-GUIDE.md','COMPONENT-TERMS.txt','UPDATES-1.1.0.md')) { Copy-Item -LiteralPath (Join-Path $project "docs\$file") -Destination (Join-Path $output "Docs\$file") }
Copy-Item -LiteralPath (Join-Path $project 'docs\RELEASE-NOTES-1.1.0.md') -Destination (Join-Path $output 'Docs\RELEASE-NOTES.md')
Copy-Item -LiteralPath (Join-Path $project 'docs\DATA.txt') -Destination (Join-Path $output 'Data\README.txt')
Copy-Item -LiteralPath (Join-Path $project 'docs\MODELS.txt') -Destination (Join-Path $output 'Models\README.txt')
Copy-Item -LiteralPath $bindingFile -Destination (Join-Path $output 'Assets\release-engine.json')
New-Item -ItemType File -Path (Join-Path $output 'portable.mode') | Out-Null
$outputPrefix = [IO.Path]::GetFullPath($output) + [IO.Path]::DirectorySeparatorChar
foreach ($file in Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Extension -in @('.pdb','.dmp') -or $_.Name -like '*.staticwebassets.*.json' }) {
    if (!$file.FullName.StartsWith($outputPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected output cleanup path.' }
    Remove-Item -LiteralPath $file.FullName -Force
}
& icacls $output /grant "${OwnerAccount}:(OI)(CI)F" /T /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot preserve interactive user access to this package.' }
& (Join-Path $PSScriptRoot 'product-check.ps1') -PackagePath $output
if ($LASTEXITCODE -ne 0) { throw 'Product package validation failed.' }
$zip = $archivePath
Save-BuildStage 'Creating Portable archive'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
$installerInfo = $null
$qaInstallerInfo = $null
if ($Release) {
    Save-BuildStage 'Compiling production Setup'
    $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 is required to build both supported update package types.' }
    & $iscc '/DAppVersion=1.1.0' "/DPayloadRoot=$output" "/DDistRoot=$line" (Join-Path $project 'installer\NInferEZManager.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Release installer compilation failed.' }
    $installer = Join-Path $line 'Installer\NInferEZ-Manager-Setup-1.1.0.exe'
    $installerInfo = [ordered]@{name=[IO.Path]::GetFileName($installer);path=$installer;bytes=(Get-Item -LiteralPath $installer).Length;sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()}
    if (!$SkipQaInstaller) {
        Save-BuildStage 'Compiling isolated QA Setup'
        $qaDist = Join-Path $line 'installer-qa'
        & $iscc '/DAppVersion=1.1.0' "/DPayloadRoot=$output" "/DDistRoot=$qaDist" '/DProductAppId=NInferEZ-Manager-110-Isolated-QA' '/DProductAppName=NInferEZ Manager Isolated QA' '/DPackageSuffix=-QA' '/DQATestPackage=1' (Join-Path $project 'installer\NInferEZManager.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Isolated QA installer compilation failed.' }
        $qaInstaller = Join-Path $qaDist 'Installer\NInferEZ-Manager-Setup-1.1.0-QA.exe'
        $qaInstallerInfo = [ordered]@{name=[IO.Path]::GetFileName($qaInstaller);path=$qaInstaller;bytes=(Get-Item -LiteralPath $qaInstaller).Length;sha256=(Get-FileHash -LiteralPath $qaInstaller -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    if ((Get-Item -LiteralPath $zip).Length -gt 1GB -or $installerInfo.bytes -gt 1GB) { throw 'A release package exceeds the existing Manager updater 1 GiB limit.' }
}
$info = [ordered]@{ product='NInferEZ Manager';version='1.1.0';buildId=$BuildId;releaseApproved=[bool]$binding.approved;engineConnected=[bool]$binding.approved;converterSource='official Manager v1.0.0';converterArchiveSha256='28adfba64dd26c1e42845532177772377ab6741ab802d69e7131e13907670f7c';archive=(Split-Path -Leaf $zip);bytes=(Get-Item -LiteralPath $zip).Length;sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() }
$info.installer = $installerInfo
$info.qaInstaller = $qaInstallerInfo
$info.backendExecutable = Join-Path $backend 'NInferManager.Backend.exe'
$info.backendExecutableSha256 = (Get-FileHash -LiteralPath $info.backendExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
$info.backendAssembly = $backendAssembly
$info.backendAssemblySha256 = (Get-FileHash -LiteralPath $backendAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
$info.contractsAssembly = $contractsAssembly
$info.contractsAssemblySha256 = (Get-FileHash -LiteralPath $contractsAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
$info | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $line "Manager-$BuildId-build.json") -Encoding utf8
$buildState.complete=$true
Save-BuildStage 'Complete'
Write-Output "Prepared $output"
