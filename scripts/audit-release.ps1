param([Parameter(Mandatory=$true)][string]$Path)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $Path).Path
$forbiddenText = @('C:\Users\','D:\space\','github_pat_','ghp_','Bearer eyJ')
$textExtensions = @('.txt','.json','.xml','.config','.md','.ps1','.cmd','.bat','.yml','.yaml','.toml','.ini','.log')
$forbiddenNames = @('settings.json','catalog.cache.json','manager.log','NInferEZ-Manager-crash.txt')
$hits = [Collections.Generic.List[string]]::new()

Get-ChildItem -LiteralPath $resolved -File -Recurse | ForEach-Object {
    $file = $_
    if ($forbiddenNames -contains $file.Name -or $file.Extension -in @('.pdb','.ninfer','.gguf','.dmp')) {
        $hits.Add("Runtime/user artifact: $($file.FullName)")
    }
    if ($textExtensions -contains $file.Extension) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if ($file.FullName.EndsWith('Converter\runtime\python\Lib\venv\scripts\common\Activate.ps1', [StringComparison]::OrdinalIgnoreCase)) {
            $content = $content.Replace('C:\Users\MyUser\Common\.venv','<standard-library-example>')
        }
        foreach ($pattern in $forbiddenText) {
            if ($content -and $content.Contains($pattern, [StringComparison]::OrdinalIgnoreCase)) {
                $hits.Add("Private text '$pattern': $($file.FullName)")
            }
        }
        if ($content -match '\bhf_[A-Za-z0-9]{12,}\b') {
            $hits.Add("Potential download credential: $($file.FullName)")
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
