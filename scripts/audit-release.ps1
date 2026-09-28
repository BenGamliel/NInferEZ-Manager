param([Parameter(Mandatory=$true)][string]$Path)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $Path).Path
$forbiddenText = @('C:\Users\','D:\space\','github_pat_','ghp_','hf_','Bearer eyJ')
$textExtensions = @('.txt','.json','.xml','.config','.md','.ps1','.cmd','.bat','.yml','.yaml','.toml','.ini','.log')
$forbiddenNames = @('settings.json','catalog.cache.json','manager.log','NInferEZ-Manager-crash.txt')
$hits = [Collections.Generic.List[string]]::new()

Get-ChildItem -LiteralPath $resolved -File -Recurse | ForEach-Object {
    $file = $_
    if ($forbiddenNames -contains $file.Name -or $file.Extension -in @('.pdb','.ninfer','.gguf','.dmp')) {
        $hits.Add("Runtime/user artifact: $($file.FullName)")
    }
    if ($textExtensions -contains $file.Extension) {
        foreach ($pattern in $forbiddenText) {
            if (Select-String -LiteralPath $file.FullName -Pattern $pattern -SimpleMatch -Quiet -ErrorAction SilentlyContinue) {
                $hits.Add("Private text '$pattern': $($file.FullName)")
            }
        }
    }
}

$dataFiles = @(Get-ChildItem -LiteralPath (Join-Path $resolved 'Data') -File -Recurse -ErrorAction SilentlyContinue)
$modelFiles = @(Get-ChildItem -LiteralPath (Join-Path $resolved 'Models') -File -Recurse -ErrorAction SilentlyContinue)
if ($dataFiles.Count -ne 1 -or $dataFiles[0].Name -ne 'README.txt') { $hits.Add('Data must contain only README.txt in a release.') }
if ($modelFiles.Count -ne 1 -or $modelFiles[0].Name -ne 'README.txt') { $hits.Add('Models must contain only README.txt in a release.') }

if ($hits.Count) {
    $hits | ForEach-Object { Write-Error $_ }
    throw "Release privacy and cleanliness audit failed."
}
Write-Host "Release audit passed for $resolved"
