param()
$ErrorActionPreference='Stop'
$preview=Split-Path -Parent $PSScriptRoot
$release=Split-Path -Parent (Split-Path -Parent $preview)
$workspace=Split-Path -Parent (Split-Path -Parent $release)
$existing=Join-Path $release 'artifacts\1.0.0\final\packages\Test Portable\NInferEZ Manager'
$converter=Join-Path $workspace 'NInferEZ Converter\dist\Portable'
$target=Join-Path $release 'Converter Test\NInferEZ Manager'
$stage=Join-Path $preview 'artifacts\stage'
if(Test-Path -LiteralPath $target){throw 'Converter Test already exists; inspect it before replacing anything.'}
foreach($item in @((Join-Path $stage 'NInferEZ Manager.exe'),(Join-Path $stage 'Backend\NInferManager.Backend.exe'),(Join-Path $converter 'NInferEZ.Converter.Backend.exe'))){if(!(Test-Path -LiteralPath $item)){throw "Missing build input: $item"}}
$priorHash=(Get-FileHash -LiteralPath (Join-Path $existing 'Data\settings.json')).Hash
function Share-File([string]$Source,[string]$Destination){
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force|Out-Null
    try{New-Item -ItemType HardLink -Path $Destination -Target $Source -ErrorAction Stop|Out-Null}
    catch{Copy-Item -LiteralPath $Source -Destination $Destination}
}
function Share-Tree([string]$Source,[string]$Destination){
    Get-ChildItem -LiteralPath $Source -File -Recurse|ForEach-Object{
        if($_.Extension -notin @('.pdb','.dmp','.pyc')){Share-File $_.FullName (Join-Path $Destination $_.FullName.Substring($Source.Length+1))}
    }
}
New-Item -ItemType Directory -Path $target -Force|Out-Null
foreach($folder in @('Assets','Docs','Engine','Models')){
    Share-Tree (Join-Path $existing $folder) (Join-Path $target $folder)
}
foreach($file in Get-ChildItem -LiteralPath $stage -File){if($file.Extension -notin @('.pdb','.dmp')){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target $file.Name)}}
Get-ChildItem -LiteralPath (Join-Path $stage 'Backend') -File -Recurse|ForEach-Object{
    if($_.Extension -notin @('.pdb','.dmp')){
        $out=Join-Path $target ('Backend\'+$_.FullName.Substring((Join-Path $stage 'Backend').Length+1))
        New-Item -ItemType Directory -Path (Split-Path -Parent $out) -Force|Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $out
    }
}
Copy-Item -LiteralPath (Join-Path $existing 'portable.mode') -Destination $target
New-Item -ItemType Directory -Path (Join-Path $target 'Data') -Force|Out-Null
foreach($file in @('settings.json','catalog.cache.json','engine-channel.cache.json','README.txt')){
    $path=Join-Path $existing "Data\$file";if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $target "Data\$file")}
}
$engines=Join-Path $existing 'Data\engines'
if(Test-Path -LiteralPath $engines){
    Get-ChildItem -LiteralPath $engines -File -Recurse|ForEach-Object{
        $out=Join-Path $target ('Data\engines\'+$_.FullName.Substring($engines.Length+1))
        if($_.Extension -eq '.exe' -or $_.Name -eq 'engine-manifest.json'){Share-File $_.FullName $out}
        else{New-Item -ItemType Directory -Path (Split-Path -Parent $out) -Force|Out-Null;Copy-Item -LiteralPath $_.FullName -Destination $out}
    }
}
$runtime=Join-Path $target 'Converter'
foreach($folder in @('app','runtime','licenses','docs')){Share-Tree (Join-Path $converter $folder) (Join-Path $runtime $folder)}
foreach($file in @('NInferEZ.Converter.Backend.exe','LICENSE','README.md')){Share-File (Join-Path $converter $file) (Join-Path $runtime $file)}
if(Test-Path -LiteralPath (Join-Path $runtime 'NInferEZ.Converter.exe')){throw 'The standalone converter frontend must not be bundled.'}
if($priorHash -ne (Get-FileHash -LiteralPath (Join-Path $existing 'Data\settings.json')).Hash){throw 'Existing settings changed unexpectedly.'}
Write-Host "Separate converter test Portable prepared: $target"
