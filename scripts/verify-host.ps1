param([switch]$SkipRestore)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $projectRoot 'artifacts\verification'
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$results = [Collections.Generic.List[object]]::new()
$projects = @('NInferManager.Backend.Tests','NInferManager.Update.Tests','NInferManager.Control.Tests')
foreach ($project in $projects) {
    $projectPath = Join-Path $projectRoot "tests\$project\$project.csproj"
    $arguments = @('build',$projectPath,'-c','Release','-p:BuildInParallel=false','-m:1')
    if ($SkipRestore) { $arguments += '--no-restore' }
    else { $arguments += "-p:RestoreConfigFile=$(Join-Path $projectRoot 'NuGet.Config')" }
    $buildOutput = @(& dotnet @arguments 2>&1)
    $buildExit = $LASTEXITCODE
    [IO.File]::WriteAllLines((Join-Path $resultDirectory "$project-build.txt"), [string[]]$buildOutput, [Text.UTF8Encoding]::new($false))
    $buildOutput | ForEach-Object { Write-Host $_ }
    if ($buildExit -ne 0) { throw "$project compilation failed ($buildExit)." }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $output = @(& dotnet run --project $projectPath -c Release --no-build --no-restore 2>&1)
    $testExit = $LASTEXITCODE
    $timer.Stop()
    [IO.File]::WriteAllLines((Join-Path $resultDirectory "$project.txt"), [string[]]$output, [Text.UTF8Encoding]::new($false))
    $output | ForEach-Object { Write-Host $_ }
    $passed = @($output | Where-Object { "$_" -match '^PASS(?::|\s)' }).Count
    $results.Add([ordered]@{ project=$project; exitCode=$testExit; passedChecks=$passed; seconds=[Math]::Round($timer.Elapsed.TotalSeconds,2) })
    if ($testExit -ne 0) { throw "$project regression tests failed ($testExit)." }
}
& (Join-Path $PSScriptRoot 'product-check.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Static product checks failed.' }
$totalChecks = 0
foreach ($result in $results) { $totalChecks += [int]$result['passedChecks'] }
$report = [ordered]@{ productVersion='0.0.1'; testedAtUtc=[DateTimeOffset]::UtcNow.ToString('O'); gpuTests=$false; nativeUiTests=$false; results=@($results); totalPassedChecks=$totalChecks }
[IO.File]::WriteAllText((Join-Path $resultDirectory 'host-results.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Host verification passed. Evidence: $resultDirectory"
