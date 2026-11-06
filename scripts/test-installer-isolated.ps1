param([string]$OwnerAccount='BENGS_PC\2beng')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$id=[Guid]::NewGuid().ToString('N')
$fixture=[IO.Path]::GetFullPath((Join-Path $project "artifacts\installer-host\$id"))
$payload=Join-Path $fixture 'payload'
$target=Join-Path $fixture 'installed'
$iscc=Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if(!(Test-Path -LiteralPath $iscc)){throw 'Inno Setup 6 is required.'}
$appId="NInferEZ-Manager-110-Installer-Fixture-$id"
$appName="NInferEZ Manager Installer Fixture $id"
$productionKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{1188B3EB-71BB-4F74-AF89-CFC3B92F227C}_is1'
function RegistrySnapshot([string]$Key){
    if(!(Test-Path -LiteralPath $Key)){return 'missing'}
    return (Get-ItemProperty -LiteralPath $Key | Select-Object -Property * -ExcludeProperty PSPath,PSParentPath,PSChildName,PSDrive,PSProvider | ConvertTo-Json -Depth 4 -Compress)
}
$registrationBefore=RegistrySnapshot $productionKey
New-Item -ItemType Directory -Path (Join-Path $payload 'Backend'),(Join-Path $payload 'Docs') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $payload 'NInferEZ Manager.exe') -Value 'fixture executable; never launched' -Encoding utf8
Set-Content -LiteralPath (Join-Path $payload 'Backend\NInferManager.Backend.exe') -Value 'fixture backend v1; never launched' -Encoding utf8
Set-Content -LiteralPath (Join-Path $payload 'Docs\COMPONENT-TERMS.txt') -Value 'Installer test fixture.' -Encoding utf8
New-Item -ItemType File -Path (Join-Path $payload 'portable.mode') | Out-Null
$setup=$null
$installed=$false
$passed=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message};$passed.Add($Message);Write-Output "PASS: $Message"}
function Build-Setup {
    & $iscc '/Q' '/DAppVersion=1.1.0' "/DPayloadRoot=$payload" "/DDistRoot=$fixture" "/DProductAppId=$appId" "/DProductAppName=$appName" '/DPackageSuffix=-Fixture' '/DQATestPackage=1' (Join-Path $project 'installer\NInferEZManager.iss')
    if($LASTEXITCODE -ne 0){throw 'Isolated installer compilation failed.'}
}
function Install-Setup {
    $arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',('/DIR="'+$target+'"'))
    $process=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if($process.ExitCode -ne 0){throw "Isolated setup returned $($process.ExitCode)."}
}
try {
    Build-Setup
    $setup=Join-Path $fixture 'Installer\NInferEZ-Manager-Setup-1.1.0-Fixture.exe'
    Install-Setup;$installed=$true
    Check (!(Test-Path -LiteralPath (Join-Path $target 'portable.mode'))) 'Installed layout excludes portable.mode even when the portable source contains it'
    Check (Test-Path -LiteralPath (Join-Path $target 'Backend\NInferManager.Backend.exe')) 'Installed layout contains the Manager backend'
    foreach($folder in @('Data','Models','Engine')){New-Item -ItemType Directory -Path (Join-Path $target $folder) -Force | Out-Null}
    Set-Content -LiteralPath (Join-Path $target 'Data\settings.json') -Value 'preserved fixture settings'
    Set-Content -LiteralPath (Join-Path $target 'Models\fixture.ninfer') -Value 'preserved fixture model'
    Set-Content -LiteralPath (Join-Path $target 'Engine\ninfer-serve.exe') -Value 'preserved fixture engine'
    Set-Content -LiteralPath (Join-Path $target 'Backend\stale.txt') -Value 'obsolete application component'
    New-Item -ItemType File -Path (Join-Path $target 'portable.mode') | Out-Null
    Set-Content -LiteralPath (Join-Path $payload 'Backend\NInferManager.Backend.exe') -Value 'fixture backend v2; never launched' -Encoding utf8
    Build-Setup;Install-Setup
    Check (!(Test-Path -LiteralPath (Join-Path $target 'portable.mode'))) 'Setup removes a pre-existing portable marker when transitioning the target to installed updates'
    Check ((Get-Content -LiteralPath (Join-Path $target 'Backend\NInferManager.Backend.exe') -Raw).Trim() -eq 'fixture backend v2; never launched') 'Silent installed update replaces the application backend'
    Check (!(Test-Path -LiteralPath (Join-Path $target 'Backend\stale.txt'))) 'Silent installed update removes obsolete application component files'
    Check ((Get-Content -LiteralPath (Join-Path $target 'Data\settings.json') -Raw).Trim() -eq 'preserved fixture settings') 'Installed update preserves settings'
    Check ((Get-Content -LiteralPath (Join-Path $target 'Models\fixture.ninfer') -Raw).Trim() -eq 'preserved fixture model') 'Installed update preserves model files'
    Check ((Get-Content -LiteralPath (Join-Path $target 'Engine\ninfer-serve.exe') -Raw).Trim() -eq 'preserved fixture engine') 'Installed update preserves selected engine files'
    Check ((RegistrySnapshot $productionKey) -eq $registrationBefore) 'Isolated setup does not modify the production installation registration'
} finally {
    if($installed -and (Test-Path -LiteralPath (Join-Path $target 'unins000.exe'))){
        $uninstall=Start-Process -FilePath (Join-Path $target 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru -Wait
        if($uninstall.ExitCode -ne 0){throw 'Isolated fixture uninstall failed.'}
    }
    if((RegistrySnapshot $productionKey) -ne $registrationBefore){throw 'Production installation registration changed unexpectedly.'}
    & icacls $fixture /grant "${OwnerAccount}:(OI)(CI)F" /T /Q | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Could not preserve user access to installer test evidence.'}
}
[ordered]@{checksPassed=$passed.Count;checks=$passed;productionInstallationUntouched=$true;applicationOrEngineLaunched=$false;fixture=$fixture} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $fixture 'result.json') -Encoding utf8
Write-Output "Installer evidence: $fixture"
