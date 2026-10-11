param([Parameter(Mandatory=$true)][string]$OwnerAccount)
$ErrorActionPreference='Stop'
$preview=Split-Path -Parent $PSScriptRoot
$release=Split-Path -Parent (Split-Path -Parent $preview)
$workspace=Split-Path -Parent (Split-Path -Parent $release)
$existing=Join-Path $release 'Converter Test\NInferEZ Manager'
$converter=Join-Path $workspace 'NInferEZ Converter\dist\Portable'
$target=Join-Path $release 'Converter Wizard Test\NInferEZ Manager'
$stage=Join-Path $preview 'artifacts\wizard-stage'
if((Test-Path -LiteralPath $target) -and @(Get-ChildItem -LiteralPath $target -File -Recurse).Count -gt 0){throw 'The separate Wizard test already exists. Do not replace it without reviewing it.'}
foreach($file in @((Join-Path $stage 'NInferEZ Manager.exe'),(Join-Path $stage 'Backend\NInferManager.Backend.exe'),(Join-Path $converter 'app\backend\planning.py'))){if(!(Test-Path -LiteralPath $file)){throw "Missing build input: $file"}}
$before=@('NInferEZ Manager.exe','Backend\NInferManager.Backend.exe','Data\settings.json')|ForEach-Object {Get-FileHash -LiteralPath (Join-Path $existing $_)}
function Share-Tree([string]$Source,[string]$Destination,[switch]$Mutable){
    New-Item -ItemType Directory -Path $Destination -Force|Out-Null
    Get-ChildItem -LiteralPath $Source -File -Recurse|Where-Object {$_.Extension -notin @('.pdb','.dmp','.pyc')}|ForEach-Object {
        $inputFile=$_.FullName
        $out=Join-Path $Destination $inputFile.Substring($Source.Length+1)
        New-Item -ItemType Directory -Path (Split-Path -Parent $out) -Force|Out-Null
        if($Mutable){Copy-Item -LiteralPath $inputFile -Destination $out}
        else{try{New-Item -ItemType HardLink -Path $out -Target $inputFile -ErrorAction Stop|Out-Null}catch{Copy-Item -LiteralPath $inputFile -Destination $out}}
    }
}
New-Item -ItemType Directory -Path $target -Force|Out-Null
foreach($folder in @('Engine','Models')){Share-Tree (Join-Path $existing $folder) (Join-Path $target $folder)}
Share-Tree (Join-Path $existing 'Data') (Join-Path $target 'Data') -Mutable
Share-Tree $stage $target -Mutable
Copy-Item -LiteralPath (Join-Path $existing 'portable.mode') -Destination $target
Share-Tree (Join-Path $existing 'Docs') (Join-Path $target 'Docs') -Mutable
Copy-Item -LiteralPath (Join-Path $preview 'docs\CONVERTER.md') -Destination (Join-Path $target 'Docs\CONVERTER.md')
foreach($folder in @('app','runtime','licenses','docs')){Share-Tree (Join-Path $converter $folder) (Join-Path $target "Converter\$folder") -Mutable:($folder-eq'app')}
foreach($file in @('NInferEZ.Converter.Backend.exe','LICENSE','README.md')){
    Copy-Item -LiteralPath (Join-Path $converter $file) -Destination (Join-Path $target "Converter\$file")
}
if(Test-Path -LiteralPath (Join-Path $target 'Converter\NInferEZ.Converter.exe')){throw 'Standalone frontend must not be bundled.'}
foreach($hash in $before){if((Get-FileHash -LiteralPath $hash.Path).Hash-ne$hash.Hash){throw "The prior test was changed: $($hash.Path)"}}
# Preserve interactive-user ownership on all new source and package output.
$human=$OwnerAccount
& icacls $target /grant "${human}:(OI)(CI)F" /T /Q
if($LASTEXITCODE-ne0){throw 'Failed to preserve interactive-user access.'}
Write-Host "Separate Wizard/Manual test Portable ready: $target"
