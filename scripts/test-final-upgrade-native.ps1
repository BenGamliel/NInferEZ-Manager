param(
    [Parameter(Mandatory)][ValidateSet('Prepare','PortableSuccess','PortableRollback','PortableRecovery','Installed','StopQa')][string]$Mode,
    [ValidatePattern('^release-\d{3}$')][string]$BuildId='release-002',
    [ValidatePattern('^[a-z0-9]{1,12}$')][string]$RunId='r2',
    [switch]$KeepUi
)
$ErrorActionPreference='Stop'
$manager=Split-Path -Parent $PSScriptRoot
$line=Split-Path -Parent $manager
$qa=Join-Path $line 'qa110'
$build=Get-Content -LiteralPath (Join-Path $line "Manager-$BuildId-build.json") -Raw|ConvertFrom-Json
$oldArchive=Join-Path $line 'dependencies/NInferEZ-Manager-Portable-1.0.0.zip'
$newArchive=Join-Path $line $build.archive
$oldPayload=Join-Path $qa 'old-payload'
$oldExe=Join-Path $oldPayload 'NInferEZ Manager.exe'
$oldBackend=Join-Path $oldPayload 'Backend/NInferManager.Backend.exe'
$newExe=Join-Path $line "Manager $BuildId/NInferEZ Manager.exe"
$oldSetup=Join-Path $qa 'setup-old/Installer/NInferEZ-Manager-Setup-1.0.0-QA.exe'
$appId='NInferEZ-Manager-110-Isolated-QA'
$appName='NInferEZ Manager Isolated QA'

function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Check([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Save([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 9),[Text.UTF8Encoding]::new($false))}
function Snapshot-Registration([string]$Path){
    if(!(Test-Path -LiteralPath $Path)){return ''}
    $item=Get-Item -LiteralPath $Path
    $values=[ordered]@{}
    foreach($name in ($item.GetValueNames()|Sort-Object)){$values[$name]=$item.GetValue($name)}
    return ($values|ConvertTo-Json -Depth 5 -Compress)
}
function Start-Owned([string]$File,[string[]]$Arguments,[bool]$Capture=$false){
    $start=[Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.WorkingDirectory=Split-Path -Parent $File
    if($Capture){$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true}
    foreach($arg in $Arguments){$start.ArgumentList.Add($arg)}
    [Diagnostics.Process]::Start($start)
}
function Find-App([string]$Path){@(Get-CimInstance Win32_Process -Filter "Name='NInferEZ Manager.exe'"|Where-Object {$_.ExecutablePath -ieq $Path})}
function Stop-App([string]$Path){
    Check ([IO.Path]::GetFullPath($Path).StartsWith($qa+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Refusing to stop an application outside the owned QA tree.'
    foreach($found in (Find-App $Path)){
        $process=[Diagnostics.Process]::GetProcessById($found.ProcessId)
        if(!$process.HasExited -and $process.MainModule.FileName -ieq $Path){$process.Kill($true);$null=$process.WaitForExit(10000)}
        $process.Dispose()
    }
}
function Wait-Until([scriptblock]$Predicate,[int]$Seconds,[string]$Failure){
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.Elapsed.TotalSeconds -lt $Seconds){if(& $Predicate){return};Start-Sleep -Milliseconds 200}
    throw $Failure
}
function Setup-Env([string]$Root){
    $env:NINFEREZ_MANAGER_DATA_ROOT=Join-Path $Root 'Data'
    $env:NINFEREZ_MANAGER_MODELS_ROOT=Join-Path $Root 'Models'
    $env:NINFEREZ_MANAGER_ENGINE_ROOT=Join-Path $Root 'Engine'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $Root 'dotnet'
    New-Item -ItemType Directory -Path $env:NINFEREZ_MANAGER_DATA_ROOT,$env:NINFEREZ_MANAGER_MODELS_ROOT,$env:NINFEREZ_MANAGER_ENGINE_ROOT -Force|Out-Null
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
    Save (Join-Path $env:NINFEREZ_MANAGER_DATA_ROOT 'settings.json') @{autoCheckCatalog=$false;autoCheckUpdates=$false;autoCheckEngines=$false;startMinimized=$false;closeToTray=$false;publicPort=$port;allowLocalNetwork=$false;lockPublicPort=$true;firstRunCompleted=$false}
    [IO.File]::WriteAllText((Join-Path $env:NINFEREZ_MANAGER_MODELS_ROOT 'existing.ninfer'),'preserved QA model fixture')
    [IO.File]::WriteAllText((Join-Path $env:NINFEREZ_MANAGER_ENGINE_ROOT 'ninfer-serve.exe'),'preserved QA engine fixture')
    return [ordered]@{settings=(Join-Path $env:NINFEREZ_MANAGER_DATA_ROOT 'settings.json');model=(Join-Path $env:NINFEREZ_MANAGER_MODELS_ROOT 'existing.ninfer');engine=(Join-Path $env:NINFEREZ_MANAGER_ENGINE_ROOT 'ninfer-serve.exe');log=(Join-Path $env:NINFEREZ_MANAGER_DATA_ROOT 'Logs/manager.log');port=$port}
}
function Seed-VerifiedEngine([string]$Root){
    $evidence=Get-Content -LiteralPath (Join-Path $qa "$RunId-engine-install-sm120a.json") -Raw|ConvertFrom-Json
    Check ($evidence.coordinatorReady -and $evidence.assemblyBytesPresentInFinalExecutable -and $evidence.backendAssemblySha256 -eq $build.backendAssemblySha256 -and $evidence.contractsAssemblySha256 -eq $build.contractsAssemblySha256 -and $evidence.installedExecutableSha256 -eq 'dbfb20eb1c5d8641559227e353d613a61443ec7375bbbb73a2faf0644d9e846a') 'A genuine exact-assembly engine installation is required.'
    $engineRoot=Join-Path $Root 'Data/engines'
    New-Item -ItemType Directory -Path $engineRoot|Out-Null
    Copy-Item -LiteralPath (Join-Path $qa "$RunId-replay-sm120a/Data/engines/1.1.0") -Destination $engineRoot -Recurse
}
function Sentinel-Hashes($Paths){[ordered]@{model=(Hash $Paths.model);engine=(Hash $Paths.engine)}}
function Assert-Preserved($Before,$Paths){
    $after=Sentinel-Hashes $Paths
    foreach($key in $Before.Keys){Check ($after[$key] -eq $Before[$key]) "Preserved QA $key changed during the update."}
    $original=Get-Content -LiteralPath (Join-Path $root 'settings-before.json') -Raw|ConvertFrom-Json -AsHashtable
    $updated=Get-Content -LiteralPath $Paths.settings -Raw|ConvertFrom-Json -AsHashtable
    foreach($key in $original.Keys){Check ($updated.Contains($key) -and $updated[$key] -ceq $original[$key]) "Preserved QA setting $key changed during the update."}
    $after.settingsValuesPreserved=$true
    $after
}

if($Mode -eq 'StopQa'){
    foreach($folder in @('portable-success','portable-rollback','installed')){Stop-App (Join-Path $qa "$RunId-$folder/App/NInferEZ Manager.exe")}
    return
}

if($Mode -eq 'Prepare'){
    Check (!(Test-Path -LiteralPath $oldPayload)) 'The old payload fixture already exists; preserve it.'
    Check ((Hash $oldArchive) -eq '28adfba64dd26c1e42845532177772377ab6741ab802d69e7131e13907670f7c') 'The historical official Manager archive changed.'
    New-Item -ItemType Directory -Path $oldPayload|Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::OpenRead($oldArchive)
    try{
        foreach($entry in $archive.Entries){
            $name=$entry.FullName.Replace('\','/');$part=$name.Split('/')[0]
            if($part -notin @('NInferEZ Manager.exe','Backend','Assets','Docs') -or $name.EndsWith('/')){continue}
            $path=[IO.Path]::GetFullPath((Join-Path $oldPayload $name))
            Check ($path.StartsWith($oldPayload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Unexpected archive path.'
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force|Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$path,$false)
        }
    }finally{$archive.Dispose()}
    Check ((Get-Item -LiteralPath $oldExe).VersionInfo.ProductVersion -like '1.0.0*') 'The old application must be version 1.0.0.'
    Check ((Get-Item -LiteralPath $oldBackend).VersionInfo.ProductVersion -like '1.0.0*') 'The old update helper must be version 1.0.0.'
    $iscc=Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
    & $iscc '/DAppVersion=1.0.0' "/DPayloadRoot=$oldPayload" "/DDistRoot=$(Join-Path $qa 'setup-old')" "/DProductAppId=$appId" "/DProductAppName=$appName" '/DPackageSuffix=-QA' '/DQATestPackage=1' (Join-Path $manager 'installer/NInferEZManager.iss')
    Check ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $oldSetup)) 'Old-payload isolated QA installer compilation failed.'
    Save (Join-Path $qa 'old-native-inputs.json') @{officialPortableSha256=(Hash $oldArchive);originalUiSha256=(Hash $oldExe);originalHelperSha256=(Hash $oldBackend);isolatedOldSetupSha256=(Hash $oldSetup)}
    return
}

Check (@(Get-CimInstance Win32_Process -Filter "Name='NInferEZ Manager.exe'").Count -eq 0) 'A Manager UI already owns the single-instance slot. No other application was stopped.'
if($Mode -eq 'PortableRecovery'){
    $root=Join-Path $qa "$RunId-portable-rollback"
    $target=Join-Path $root 'App'
    $targetExe=Join-Path $target 'NInferEZ Manager.exe'
    $targetBackend=Join-Path $target 'Backend/NInferManager.Backend.exe'
    $journal=Join-Path $target '.update-recovery/journal.json'
    $recovery=Join-Path $target '.update-recovery/recover.exe'
    $failed=Get-Content -LiteralPath (Join-Path $root 'result.json') -Raw|ConvertFrom-Json
    $pending=Get-Content -LiteralPath $journal -Raw|ConvertFrom-Json
    Check ($failed.passed -eq $false -and $failed.portableArchiveSha256 -eq $build.sha256 -and $pending.Target -ieq $target -and $pending.State -eq 'AwaitingConfirmation') 'The isolated rollback fixture must retain an unconfirmed update.'
    Check ((Hash $recovery) -eq (Hash $oldBackend) -and (Hash (Join-Path $target '.update-recovery/backup/NInferEZ Manager.exe')) -eq (Hash $oldExe)) 'The 1.0 recovery helper and backup must be exact original bytes.'
    $env:NINFEREZ_MANAGER_DATA_ROOT=Join-Path $root 'Data'
    $env:NINFEREZ_MANAGER_MODELS_ROOT=Join-Path $root 'Models'
    $env:NINFEREZ_MANAGER_ENGINE_ROOT=Join-Path $root 'Engine'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $root 'dotnet'
    $paths=@{settings=(Join-Path $root 'Data/settings.json');model=(Join-Path $root 'Models/existing.ninfer');engine=(Join-Path $root 'Engine/ninfer-serve.exe')}
    $before=Sentinel-Hashes $paths
    $settings=Get-Content -LiteralPath $paths.settings -Raw|ConvertFrom-Json
    $report=[ordered]@{mode=$Mode;buildId=$BuildId;passed=$false;originalHelperSha256=(Hash $recovery);portableArchiveSha256=$build.sha256;legacyAutomaticRollbackFailurePreserved=$true}
    try{
        & $recovery '--recover-portable-update' '--target' $target
        Check ($LASTEXITCODE -eq 0) 'The original explicit recovery command did not succeed.'
        Check ((Hash $targetExe) -eq (Hash $oldExe) -and (Hash $targetBackend) -eq (Hash $oldBackend)) 'Explicit recovery did not restore exact 1.0.0 application and backend.'
        Check ((Get-Content -LiteralPath $journal -Raw|ConvertFrom-Json).State -eq 'RolledBack') 'The original helper did not record a completed rollback.'
        $report.previousUiSha256=Hash $targetExe
        $report.previousBackendSha256=Hash $targetBackend
        $report.exactOldPayloadRestored=$true
        $report.rolledBackJournal=$true
        $null=Start-Owned $targetExe @()
        Wait-Until { try{(Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.publicPort)/health" -TimeoutSec 2).status -eq 'ok'}catch{$false} } 30 'The recovered Manager 1.0.0 public backend was not healthy.'
        Check (@(Find-App $targetExe).Count -eq 1) 'The recovered 1.0.0 UI was not running.'
        $report.preservedFiles=Assert-Preserved $before $paths
        $report.recoveredAppHealthy=$true
        $report.passed=$true
    }finally{
        Stop-App $targetExe
        Save (Join-Path $root 'manual-recovery-result.json') $report
    }
    Write-Output "PASS: $Mode. Evidence: $(Join-Path $root 'manual-recovery-result.json')"
    return
}
$folder=switch($Mode){'PortableSuccess'{'portable-success'};'PortableRollback'{'portable-rollback'};'Installed'{'installed'}}
$root=Join-Path $qa "$RunId-$folder"
Check (!(Test-Path -LiteralPath $root)) 'This native QA root already exists; preserve the prior result.'
$target=Join-Path $root 'App';$targetExe=Join-Path $target 'NInferEZ Manager.exe'
$targetBackend=Join-Path $target 'Backend/NInferManager.Backend.exe'
$paths=Setup-Env $root
Copy-Item -LiteralPath $paths.settings -Destination (Join-Path $root 'settings-before.json')
$before=Sentinel-Hashes $paths
$helper=Join-Path $root 'original-1.0-helper.exe'
Copy-Item -LiteralPath $oldBackend -Destination $helper
$started=$null;$stdout=$null;$stderr=$null;$testPassed=$false
$report=[ordered]@{mode=$Mode;buildId=$BuildId;passed=$false;originalHelperSha256=(Hash $helper);portableArchiveSha256=(Hash $newArchive);newUiSha256=(Hash $newExe);newBackendSha256=$build.backendExecutableSha256;root=$root}
Check ($report.portableArchiveSha256 -eq $build.sha256) 'The final Portable archive changed.'
$registryKey="HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/${appId}_is1"
$productionKey='HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{1188B3EB-71BB-4F74-AF89-CFC3B92F227C}_is1'
$productionBefore=Snapshot-Registration $productionKey
$ownedInstall=$false
try{
    if($Mode -eq 'Installed'){
        Check (!(Test-Path -LiteralPath $registryKey)) 'The QA installer registration already exists; refusing to overwrite it.'
        Seed-VerifiedEngine $root
        $oldInstall=Start-Owned $oldSetup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$target","/LOG=$(Join-Path $root 'setup-old.log')")
        Check ($oldInstall.WaitForExit(120000) -and $oldInstall.ExitCode -eq 0) 'Old isolated Setup did not install successfully.'
        $ownedInstall=$true
        Check ((Hash $targetExe) -eq (Hash $oldExe) -and (Hash $targetBackend) -eq (Hash $oldBackend)) 'Old Setup did not install the exact official 1.0.0 app/backend.'
        Check ((Get-ItemProperty -LiteralPath $registryKey).DisplayVersion -eq '1.0.0') 'Old isolated Setup registration is not version 1.0.0.'
        Check (!(Test-Path -LiteralPath (Join-Path $target 'portable.mode'))) 'Installed helper target must be marker-free.'
        $report.qaSetupSha256=Hash $build.qaInstaller.path
        $report.productionSetupSha256=Hash $build.installer.path
        Check ($report.qaSetupSha256 -eq $build.qaInstaller.sha256) 'The QA Setup changed.'
        Check ($report.productionSetupSha256 -eq $build.installer.sha256) 'The production Setup changed.'
        $started=Start-Owned $helper @('--apply-installed-update','--installer',$build.qaInstaller.path,'--target',$target,'--ui-pid','2147483646','--backend-pid','2147483645','--sha256',$build.qaInstaller.sha256) $true
        $stdout=$started.StandardOutput.ReadToEndAsync();$stderr=$started.StandardError.ReadToEndAsync()
        Check ($started.WaitForExit(20000) -and $started.ExitCode -eq 0) 'The original installed update helper failed to verify/launch QA Setup.'
        Wait-Until { (Test-Path -LiteralPath $targetExe) -and (Get-Item -LiteralPath $targetExe).VersionInfo.ProductVersion -like '1.1.0*' -and @(Find-App $targetExe).Count -eq 1 } 120 'QA Setup did not launch the upgraded Manager.'
        Check ((Get-ItemProperty -LiteralPath $registryKey).DisplayVersion -eq '1.1.0') 'The isolated Setup registration did not upgrade to 1.1.0.'
        Check (!(Test-Path -LiteralPath (Join-Path $target 'portable.mode'))) 'The installed update entered Portable mode.'
        $report.installerRegistrationVersion='1.1.0'
    }else{
        Copy-Item -LiteralPath $oldPayload -Destination $target -Recurse
        [IO.File]::WriteAllText((Join-Path $target 'portable.mode'),'')
        foreach($component in @('Data','Models','Engine')){New-Item -ItemType Directory -Path (Join-Path $target $component) -Force|Out-Null;[IO.File]::WriteAllText((Join-Path $target "$component/qa-preserve.txt"),"preserve $component")}
        if($Mode -eq 'PortableSuccess'){Seed-VerifiedEngine $root}else{
            $cache=Join-Path $root 'Data/cache';New-Item -ItemType Directory -Path $cache -Force|Out-Null
            [IO.File]::WriteAllText((Join-Path $cache 'engine-downloads'),'fixture blocks download setup before network')
        }
        $started=Start-Owned $helper @('--apply-portable-update','--zip',$newArchive,'--target',$target,'--ui-pid','2147483646','--backend-pid','2147483645','--sha256',$build.sha256) $true
        $stdout=$started.StandardOutput.ReadToEndAsync();$stderr=$started.StandardError.ReadToEndAsync()
        Wait-Until { @(Find-App $targetExe).Count -eq 1 } 60 'The original portable helper did not relaunch the new Manager.'
        Check ((Hash $targetExe) -eq $report.newUiSha256 -and (Hash $targetBackend) -eq $build.backendExecutableSha256) 'The old helper did not install the exact final app/backend.'
        $journal=Join-Path $target '.update-recovery/journal.json'
        if($Mode -eq 'PortableSuccess'){
            Check ($started.WaitForExit(100000) -and $started.ExitCode -eq 0) 'The original portable helper failed to observe successful startup.'
            Check (!(Test-Path -LiteralPath (Split-Path -Parent $journal))) 'New UI did not acknowledge ProductUpdate Ready; recovery was retained.'
            $report.startupAcknowledged=$true;$report.productUpdateReady=$true
        }else{
            Wait-Until { (Test-Path -LiteralPath $paths.log) -and (Select-String -LiteralPath $paths.log -SimpleMatch 'NInferEZ update completion failed' -Quiet) } 30 'Expected owned filesystem failure did not reach product setup.'
            Check ((Test-Path -LiteralPath $journal) -and !(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $journal) 'launch-ack'))) 'Failed setup must retain recovery and must not acknowledge startup.'
            Copy-Item -LiteralPath $journal -Destination (Join-Path $root 'awaiting-confirmation.json')
            Stop-App $targetExe
            Check ($started.WaitForExit(30000) -and $started.ExitCode -ne 0) 'The original helper did not report failed startup after the owned new UI stopped.'
            Wait-Until { @(Find-App $targetExe).Count -eq 1 } 25 'The original helper did not relaunch the previous Manager.'
            Check ((Hash $targetExe) -eq (Hash $oldExe) -and (Hash $targetBackend) -eq (Hash $oldBackend)) 'The original helper did not restore exact old application/backend bytes.'
            Check ((Get-Content -LiteralPath $journal -Raw|ConvertFrom-Json).State -eq 'RolledBack') 'Rollback journal was not preserved.'
            Copy-Item -LiteralPath $journal -Destination (Join-Path $root 'rolled-back.json')
            $report.startupAcknowledged=$false;$report.rollbackCompleted=$true;$report.previousUiRelaunched=$true
        }
        foreach($component in @('Data','Models','Engine')){Check ((Get-Content -LiteralPath (Join-Path $target "$component/qa-preserve.txt") -Raw) -eq "preserve $component") "The original helper changed $component."}
        Check (Test-Path -LiteralPath (Join-Path $target 'portable.mode')) 'The original helper removed the Portable marker.'
    }
    if($Mode -ne 'PortableRollback'){
        Check ((Hash $targetExe) -eq $report.newUiSha256 -and (Hash $targetBackend) -eq $build.backendExecutableSha256) 'Native upgrade did not install exact final Manager payload bytes.'
        Wait-Until { try{(Invoke-RestMethod -Uri "http://127.0.0.1:$($paths.port)/health" -TimeoutSec 2).status -eq 'ok'}catch{$false} } 30 'The upgraded UI backend did not expose its isolated public health endpoint.'
        $pointer=Join-Path $root 'Data/engines/current.json'
        Wait-Until { if(!(Test-Path -LiteralPath $pointer)){return $false};$pinned=Get-Content -LiteralPath $pointer -Raw|ConvertFrom-Json;return ($pinned.engineVersion -eq '1.1.0' -and $pinned.cudaArchitecture -eq 'sm120a') } 30 'The new backend did not activate the verified RTX 5090 pin.'
        $report.productUpdateReady=$true
        $report.enginePointerActivated=$true
        $report.installedEngineSha256=Hash (Join-Path $root 'Data/engines/1.1.0/sm120a/ninfer-serve.exe')
        Check ($report.installedEngineSha256 -eq 'dbfb20eb1c5d8641559227e353d613a61443ec7375bbbb73a2faf0644d9e846a') 'The new backend activated unexpected Engine bytes.'
        $report.publicHealthVerified=$true
    }
    $report.preservedFiles=Assert-Preserved $before $paths
    $report.firstRunStillIncomplete=((Get-Content -LiteralPath $paths.settings -Raw|ConvertFrom-Json).firstRunCompleted -eq $false)
    Check $report.firstRunStillIncomplete 'The QA update must not depend on completing the first-run dialog.'
    $report.productionRegistrationPreserved=((Snapshot-Registration $productionKey) -eq $productionBefore)
    Check $report.productionRegistrationPreserved 'Production Setup registration changed unexpectedly.'
    $report.uiProcessId=(Find-App $targetExe)[0].ProcessId
    $report.passed=$true;$testPassed=$true
}finally{
    if(!$KeepUi -or !$testPassed){Stop-App $targetExe}
    if($started -and $started.HasExited){
        $report.helperExitCode=$started.ExitCode
        [IO.File]::WriteAllText((Join-Path $root 'helper-output.txt'),$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult())
    }
    if(Test-Path -LiteralPath $paths.log){Copy-Item -LiteralPath $paths.log -Destination (Join-Path $root 'manager-startup.log')}
    if($ownedInstall -and (!$KeepUi -or !$testPassed)){
        try{
            $registered=Get-ItemProperty -LiteralPath $registryKey
            Check ($registered.InstallLocation.TrimEnd('\') -ieq $target) 'Refusing to uninstall an application outside the owned QA path.'
            $uninstaller=Join-Path $target 'unins000.exe'
            Check ($registered.UninstallString -ieq ('"'+$uninstaller+'"')) 'The isolated uninstall executable is not the expected owned file.'
            $uninstall=Start-Owned $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
            $report.qaRegistrationRemoved=$uninstall.WaitForExit(30000) -and $uninstall.ExitCode -eq 0 -and !(Test-Path -LiteralPath $registryKey)
            Check ($report.qaRegistrationRemoved -and (Snapshot-Registration $productionKey) -eq $productionBefore) 'Isolated Setup cleanup did not preserve production registration.'
            $null=Assert-Preserved $before $paths
        }catch{$report.passed=$false;$report.cleanupError=$_.Exception.Message}
    }
    Save (Join-Path $root 'result.json') $report
}
Check $report.passed 'Native test or isolated cleanup did not pass.'
Write-Output "PASS: $Mode. Evidence: $(Join-Path $root 'result.json')"
