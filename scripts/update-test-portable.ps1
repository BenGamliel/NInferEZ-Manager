$ErrorActionPreference='Stop'
$previewRoot=Split-Path -Parent $PSScriptRoot
$releaseRoot=[IO.Path]::GetFullPath((Join-Path $previewRoot '..\..'))
$packages=Join-Path $releaseRoot 'artifacts\1.0.0\final\packages'
$target=Join-Path $packages 'Test Portable\NInferEZ Manager'
$stage=Join-Path $previewRoot 'artifacts\stage'
$backup=Join-Path $previewRoot 'artifacts\previous-app-announcements'
$appFiles=@('NInferEZ Manager.exe','Backend\NInferManager.Backend.exe','Backend\Assets\catalog.json')
if(!(Test-Path -LiteralPath (Join-Path $target 'portable.mode'))){throw 'The intended test Portable is missing.'}
if(Test-Path -LiteralPath $backup){throw 'An earlier update backup exists; refusing to overwrite it.'}
foreach($relative in $appFiles){
    $destination=[IO.Path]::GetFullPath((Join-Path $target $relative))
    if(!$destination.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected test replacement path.'}
    if(!(Test-Path -LiteralPath (Join-Path $stage $relative))){throw 'A staged application executable is missing.'}
    $running=Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $destination }
    if($running){throw 'Close this test Portable through its tray icon before updating. No process was stopped.'}
}
$unchangedFiles=@('Installer\NInferEZ-Manager-Setup-1.0.0.exe','NInferEZ-Manager-Portable-1.0.0.zip',
    'Portable\NInferEZ Manager\NInferEZ Manager.exe','Portable\NInferEZ Manager\Backend\NInferManager.Backend.exe',
    'Portable\NInferEZ Manager\Backend\Assets\catalog.json')
$before=@{};foreach($file in $unchangedFiles){$before[$file]=(Get-FileHash -LiteralPath (Join-Path $packages $file)).Hash}
$settingsFile=Join-Path $target 'Data\settings.json'
$settingsHash=(Get-FileHash -LiteralPath $settingsFile).Hash
New-Item -ItemType Directory -Path (Join-Path $backup 'Backend') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $backup 'Backend\Assets') -Force | Out-Null
$moved=[Collections.Generic.List[string]]::new()
try{
    foreach($relative in $appFiles){
        # Move the old directory entry first: it may share a hard link with the public Portable.
        Move-Item -LiteralPath (Join-Path $target $relative) -Destination (Join-Path $backup $relative)
        $moved.Add($relative)
        Copy-Item -LiteralPath (Join-Path $stage $relative) -Destination (Join-Path $target $relative)
        if((Get-FileHash -LiteralPath (Join-Path $stage $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash){throw 'Updated executable verification failed.'}
    }
    foreach($file in $unchangedFiles){if($before[$file] -ne (Get-FileHash -LiteralPath (Join-Path $packages $file)).Hash){throw 'A public package changed unexpectedly.'}}
    if($settingsHash -ne (Get-FileHash -LiteralPath $settingsFile).Hash){throw 'Saved model profiles changed unexpectedly.'}
}catch{
    foreach($relative in $moved){
        $path=[IO.Path]::GetFullPath((Join-Path $target $relative))
        if(!$path.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected rollback path.'}
        if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}
        Move-Item -LiteralPath (Join-Path $backup $relative) -Destination $path
    }
    throw
}
Write-Host 'Updated only the test Portable UI/backend. Public packages, model links, profiles and engine files were preserved; previous application binaries remain recoverable.'
