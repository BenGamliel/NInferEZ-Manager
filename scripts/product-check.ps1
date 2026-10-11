param(
    [string]$PackagePath = '',
    [switch]$LocalTest
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()
$passed = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) {
    if ($Condition) { $passed.Add($Message) } else { $failures.Add($Message) }
}

$props = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$productInfo = Get-Content -LiteralPath (Join-Path $projectRoot 'src/NInferManager.Backend/ProductInfo.cs') -Raw
$installer = Get-Content -LiteralPath (Join-Path $projectRoot 'installer/NInferEZManager.iss') -Raw
Check ($props -match '<Version>1\.0\.0</Version>') 'Assembly product version is 1.0.0'
Check ($productInfo -match 'const string Version\s*=\s*"1\.0\.0"') 'Backend product version is 1.0.0'
Check ($installer -match '#define AppVersion "1\.0\.0"') 'Installer product version is 1.0.0'
Check ($installer -match 'PrivilegesRequired=lowest') 'Installer is per-user without elevation'
Check ($installer -match 'MB_YESNO or MB_DEFBUTTON2') 'Uninstall data removal defaults to No'
Check ($installer -notmatch 'DelTree\(.*\{(?:userprofile|localappdata|app)\}[''"],') 'Uninstall does not recursively remove a broad root'
foreach ($name in @('README.md', 'SECURITY.md', 'LICENSE', 'docs/ECOSYSTEM.md', 'docs/ACCEPTANCE-MATRIX.md', 'docs/THIRD-PARTY-NOTICES.md', 'assets/NInferEZ.ico')) {
    Check (Test-Path -LiteralPath (Join-Path $projectRoot $name) -PathType Leaf) "Required product material: $name"
}

if ($PackagePath) {
    $payloadRoot = (Resolve-Path -LiteralPath $PackagePath).Path
    foreach ($name in @('NInferEZ Manager.exe', 'Backend/NInferManager.Backend.exe', 'Assets/NInferEZ.ico', 'Docs/LICENSE.txt', 'Docs/THIRD-PARTY-NOTICES.md', 'Docs/USER-GUIDE.md', 'Docs/RELEASE-NOTES.md')) {
        Check (Test-Path -LiteralPath (Join-Path $payloadRoot $name) -PathType Leaf) "Required packaged file: $name"
    }
    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse)
    $weightFiles = @($payloadFiles | Where-Object Extension -in @('.ninfer', '.gguf', '.safetensors'))
    Check ($weightFiles.Count -eq 0) 'Package never contains model weight copies'
    Check (@($payloadFiles | Where-Object Extension -in @('.pdb', '.dmp')).Count -eq 0) 'Package contains no debug symbols or crash dumps'
    if (-not $LocalTest) {
        $privateNames = @('settings.json', 'manager.log', 'control-token.json', 'control-token.txt', 'backend-instance.json', 'runtime.json', 'catalog.cache.json', 'engine-channel.cache.json')
        Check (@($payloadFiles | Where-Object Name -in $privateNames).Count -eq 0) 'Public package has no runtime settings/log/token/cache files'
        $textExtensions = @('.txt', '.json', '.xml', '.config', '.md', '.ps1', '.cmd', '.bat', '.yml', '.yaml', '.toml', '.ini', '.log')
        foreach ($file in $payloadFiles | Where-Object Extension -in $textExtensions) {
            if ($file.Length -gt 16MB) { $failures.Add("Unexpected oversized text artifact: $($file.Name)"); continue }
            $content = Get-Content -LiteralPath $file.FullName -Raw
            if ($file.FullName.EndsWith('Converter\runtime\python\Lib\venv\scripts\common\Activate.ps1', [StringComparison]::OrdinalIgnoreCase)) {
                $content = $content.Replace('C:\Users\MyUser\Common\.venv','<standard-library-example>')
            }
            if ($content -match '(?i)[A-Z]:[\\/]Users[\\/](?!Public[\\/]|Default[\\/])[^\\/\s]+|[A-Z]:[\\/]space[\\/]|github_pat_[A-Za-z0-9_]+|ghp_[A-Za-z0-9]+|hf_[A-Za-z0-9]{12,}|Bearer\s+eyJ[A-Za-z0-9_-]+') {
                $failures.Add("Potential personal path or credential in public text: $($file.Name)")
            }
        }
        Check (@($payloadFiles | Where-Object { $_.FullName.StartsWith((Join-Path $payloadRoot 'Data') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and $_.Name -ne 'README.txt' }).Count -eq 0) 'Public Data contains only documentation'
        Check (@($payloadFiles | Where-Object { $_.FullName.StartsWith((Join-Path $payloadRoot 'Models') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and $_.Name -ne 'README.txt' }).Count -eq 0) 'Public Models contains only documentation'
    }
    else {
        Check (Test-Path -LiteralPath (Join-Path $payloadRoot 'portable.mode')) 'Local test package is Portable'
        Check ((Test-Path -LiteralPath (Join-Path $payloadRoot 'Engine/ninfer-serve.exe')) -and (Test-Path -LiteralPath (Join-Path $payloadRoot 'Engine/ninfer-inspect.exe')) -and (Test-Path -LiteralPath (Join-Path $payloadRoot 'Engine/engine-manifest.json'))) 'Local test Portable includes engine, inspector and manifest'
    }
}

$result = [ordered]@{
    mode = if (!$PackagePath) { 'source-only' } elseif ($LocalTest) { 'local-test-package' } else { 'public-package' }
    checksPassed = $passed.Count
    failures = @($failures)
    evidence = 'Static host-only product check. Does not validate inference, UI, signing or hardware compatibility.'
}
$result | ConvertTo-Json -Depth 3
if ($failures.Count) { throw "Product check failed ($($failures.Count) findings)." }
