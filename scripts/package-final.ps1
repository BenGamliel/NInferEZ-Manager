param(
    [ValidateSet('Stage','Package')][string]$Phase = 'Stage',
    [Parameter(Mandatory=$true)][string]$OwnerAccount
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$releaseLine = Split-Path -Parent (Split-Path -Parent $project)
$workspace = Split-Path -Parent (Split-Path -Parent $releaseLine)
$release = Join-Path $releaseLine 'Release'
$payload = Join-Path $release 'Payload'
$portable = Join-Path $release 'Portable\NInferEZ Manager'
$upload = Join-Path $release 'Upload'
$converter = Join-Path $workspace 'NInferEZ Converter\dist\Portable'
$stage = Join-Path $project 'artifacts\wizard-stage'

function Copy-ReleaseTree([string]$Source,[string]$Destination,[switch]$SourceCode) {
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | ForEach-Object {
        $inputFile = $_.FullName
        $relative = $inputFile.Substring($sourceRoot.Length+1)
        if ($relative -match '(^|[\\/])__pycache__([\\/]|$)' -or $_.Extension -in @('.pdb','.dmp','.pyc')) { return }
        if ($SourceCode -and $relative -match '(^|[\\/])(\.[^\\/]+|bin|obj|artifacts)([\\/]|$)') { return }
        $outputFile = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
        # Keep editable source and the release-specific documentation cleanup independent.
        if ($SourceCode -or $relative -eq 'Lib\site-packages\torch\_dynamo\eval_frame.py') {
            Copy-Item -LiteralPath $inputFile -Destination $outputFile
            if (!$SourceCode) {
                $lines = [IO.File]::ReadAllLines($inputFile)
                $clean = @($lines | Where-Object { !$_.StartsWith('    Note - this headerdoc was authored by ', [StringComparison]::Ordinal) })
                [IO.File]::WriteAllLines($outputFile,$clean,[Text.UTF8Encoding]::new($false))
            }
        } else {
            try { New-Item -ItemType HardLink -Path $outputFile -Target $inputFile -ErrorAction Stop | Out-Null }
            catch { Copy-Item -LiteralPath $inputFile -Destination $outputFile }
        }
    }
}

function Preserve-OwnerAccess {
    & icacls $release /grant "${OwnerAccount}:(OI)(CI)F" /T /Q
    if ($LASTEXITCODE -ne 0) { throw 'Could not preserve interactive-user access to the release.' }
    $account = [Security.Principal.NTAccount]::new($OwnerAccount)
    $sid = $account.Translate([Security.Principal.SecurityIdentifier]).Value
    foreach ($path in @($release,$payload,(Join-Path $payload 'Converter\runtime\python\python.exe'))) {
        $rules = (Get-Acl -LiteralPath $path).Access
        $allowed = @($rules | Where-Object {
            $_.AccessControlType -eq 'Allow' -and
            $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq $sid -and
            ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl
        })
        if ($allowed.Count -eq 0) { throw "FullControl not verified: $path" }
    }
}

if ($Phase -eq 'Stage') {
    if (Test-Path -LiteralPath $release) { throw 'Final release directory already exists; refusing to overwrite it.' }
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($project)).AvailableFreeSpace -lt 3GB) { throw 'At least 3 GiB free space is required.' }
    foreach ($file in @((Join-Path $stage 'NInferEZ Manager.exe'),(Join-Path $stage 'Backend\NInferManager.Backend.exe'),(Join-Path $converter 'app\backend\planning.py'))) {
        if (!(Test-Path -LiteralPath $file)) { throw "Missing validated release input: $file" }
    }
    New-Item -ItemType Directory -Path $release,$payload,$upload | Out-Null
    Copy-ReleaseTree $stage $payload
    foreach ($relative in @('NInferEZ Manager.exe','Backend\NInferManager.Backend.exe')) {
        $approved = Join-Path $releaseLine "Converter Wizard Test\NInferEZ Manager\$relative"
        if ((Get-FileHash -LiteralPath (Join-Path $payload $relative)).Hash -ne (Get-FileHash -LiteralPath $approved).Hash) { throw "Approved executable differs from staging: $relative" }
    }
    foreach ($folder in @('Data','Models','Docs')) { New-Item -ItemType Directory -Path (Join-Path $payload $folder) | Out-Null }
    Copy-Item -LiteralPath (Join-Path $project 'docs\DATA.txt') -Destination (Join-Path $payload 'Data\README.txt')
    Copy-Item -LiteralPath (Join-Path $project 'docs\MODELS.txt') -Destination (Join-Path $payload 'Models\README.txt')
    Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination (Join-Path $payload 'Docs\LICENSE.txt')
    foreach ($file in @('THIRD-PARTY-NOTICES.md','USER-GUIDE.md','CONVERTER.md')) {
        Copy-Item -LiteralPath (Join-Path $project "docs\$file") -Destination (Join-Path $payload "Docs\$file")
    }
    Copy-Item -LiteralPath (Join-Path $project 'docs\RELEASE-NOTES-1.0.0.md') -Destination (Join-Path $payload 'Docs\RELEASE-NOTES.md')
    foreach ($folder in @('app','runtime','licenses','docs')) { Copy-ReleaseTree (Join-Path $converter $folder) (Join-Path $payload "Converter\$folder") }
    foreach ($file in @('NInferEZ.Converter.Backend.exe','LICENSE')) { Copy-Item -LiteralPath (Join-Path $converter $file) -Destination (Join-Path $payload "Converter\$file") }
    Copy-Item -LiteralPath (Join-Path $project 'docs\CONVERTER.md') -Destination (Join-Path $payload 'Converter\README.md')
    & (Join-Path $PSScriptRoot 'clean-brand-metadata.ps1') -Path (Join-Path $payload 'Assets\2beng2.png')
    Preserve-OwnerAccess
    Write-Host "Final payload staged: $payload"
    exit
}

if (!(Test-Path -LiteralPath $payload) -or (Test-Path -LiteralPath (Join-Path $upload 'NInferEZ-Manager-Portable-1.0.0.zip'))) { throw 'Stage the release first; packaging never overwrites a completed release.' }
& (Join-Path $PSScriptRoot 'audit-release.ps1') -Path $payload
& (Join-Path $PSScriptRoot 'product-check.ps1') -PackagePath $payload
if (Test-Path -LiteralPath (Join-Path $payload 'Converter\NInferEZ.Converter.exe')) { throw 'The standalone converter frontend must not be included.' }
Copy-ReleaseTree $payload $portable
New-Item -ItemType File -Path (Join-Path $portable 'portable.mode') | Out-Null
& (Join-Path $PSScriptRoot 'audit-release.ps1') -Path $portable
& (Join-Path $PSScriptRoot 'product-check.ps1') -PackagePath $portable

Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $upload 'NInferEZ-Manager-Portable-1.0.0.zip') -CompressionLevel Optimal
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 is required.' }
& $iscc '/DAppVersion=1.0.0' "/DPayloadRoot=$payload" "/DDistRoot=$upload" (Join-Path $project 'installer\NInferEZManager.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }

$source = Join-Path $release 'Source\NInferEZ Manager'
foreach ($folder in @('src','tests','scripts','installer','assets','feeds','docs','design')) {
    Copy-ReleaseTree (Join-Path $project $folder) (Join-Path $source $folder) -SourceCode
}
Get-ChildItem -LiteralPath $project -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $source }
Compress-Archive -Path (Join-Path $source '*') -DestinationPath (Join-Path $upload 'NInferEZ-Manager-Source-1.0.0.zip') -CompressionLevel Optimal
$hashes = Get-ChildItem -LiteralPath $upload -File -Recurse | Sort-Object FullName | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.FullName.Substring($upload.Length+1).Replace('\','/'))"
}
[IO.File]::WriteAllLines((Join-Path $upload 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Preserve-OwnerAccess
Write-Host "NInferEZ Manager 1.0.0 release packages ready: $upload"
