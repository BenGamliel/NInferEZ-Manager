param([Parameter(Mandatory=$true)][string]$BuildInfo)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$info=Get-Content -LiteralPath $BuildInfo -Raw | ConvertFrom-Json
if(!$info.releaseApproved -or !$info.engineConnected){throw 'Use the approved final Manager build evidence.'}
foreach($item in @(@($info.backendAssembly,$info.backendAssemblySha256),@($info.contractsAssembly,$info.contractsAssemblySha256))){
    if((Get-FileHash -LiteralPath $item[0] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item[1]){throw 'Final Manager component evidence changed.'}
}
dotnet build (Join-Path $project 'tests\NInferManager.EngineUpdate.Replay\NInferManager.EngineUpdate.Replay.csproj') -c Release "-p:ManagerBackendAssembly=$($info.backendAssembly)" "-p:ManagerContractsAssembly=$($info.contractsAssembly)" --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Exact-assembly engine update replay build failed.'}
Write-Output 'Replay uses final component DLLs and proves their bytes exist in the final backend executable before installing an engine.'
