param([string]$OwnerAccount='BENGS_PC\2beng')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$line=Split-Path -Parent $project
$workspace=Split-Path -Parent $line
$legacy=Join-Path $workspace 'Engines\Final\PRODUCTION\repos\manager'
$build=Join-Path $project 'artifacts\legacy-update-replay'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$props=Join-Path $build 'Directory.Build.props'
$legacyProps=[Security.SecurityElement]::Escape((Join-Path $legacy 'Directory.Build.props'))
$output=[Security.SecurityElement]::Escape($build.Replace('\','/'))
$contents=@'
<Project>
  <Import Project="__LEGACY_PROPS__" />
  <PropertyGroup>
    <BaseIntermediateOutputPath>__OUTPUT__/obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
    <BaseOutputPath>__OUTPUT__/bin/$(MSBuildProjectName)/</BaseOutputPath>
  </PropertyGroup>
</Project>
'@
$contents=$contents.Replace('__LEGACY_PROPS__',$legacyProps).Replace('__OUTPUT__',$output)
[IO.File]::WriteAllText($props,$contents,[Text.UTF8Encoding]::new($false))
$sources=@{}
foreach($relative in @('src\NInferManager.Backend\UpdateService.cs','src\NInferManager.Backend\ProductInfo.cs','src\NInferManager.Backend\AppPaths.cs','src\NInferManager.Backend\SettingsService.cs')){$sources[$relative]=(Get-FileHash -LiteralPath (Join-Path $legacy $relative)).Hash.ToLowerInvariant()}
dotnet build (Join-Path $legacy 'src\NInferManager.Backend\NInferManager.Backend.csproj') -c Release "-p:DirectoryBuildPropsPath=$props" --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Original 1.0.0 service build failed.'}
foreach($relative in $sources.Keys){if((Get-FileHash -LiteralPath (Join-Path $legacy $relative)).Hash.ToLowerInvariant() -ne $sources[$relative]){throw 'An original source file changed unexpectedly.'}}
dotnet build (Join-Path $project 'tests\NInferManager.LegacyUpdate.Replay\NInferManager.LegacyUpdate.Replay.csproj') -c Release --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Legacy replay harness build failed.'}
$assembly=Get-ChildItem -LiteralPath (Join-Path $build 'bin\NInferManager.Backend') -Filter 'NInferManager.Backend.dll' -File -Recurse | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$exe=Get-ChildItem -LiteralPath (Join-Path $project 'tests\NInferManager.LegacyUpdate.Replay\bin\Release') -Filter 'NInferManager.LegacyUpdate.Replay.exe' -File -Recurse | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
[ordered]@{originalBackendAssembly=$assembly.FullName;replayApphost=$exe.FullName;sourceHashes=$sources;originalSourceModified=$false} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $build 'build.json') -Encoding utf8
& icacls $build /grant "${OwnerAccount}:(OI)(CI)F" /T /Q | Out-Null
if($LASTEXITCODE -ne 0){throw 'Could not preserve user access to the legacy replay evidence.'}
Write-Output "Legacy replay ready: $($exe.FullName)"
Write-Output "Original service assembly: $($assembly.FullName)"
