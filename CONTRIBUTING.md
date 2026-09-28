# Contributing

NInferEZ Manager is a Windows desktop client for NInferEZ Engine. Keep changes focused, preserve the
localhost-first security model, and do not commit models, engine binaries, logs, settings or machine-
specific paths.

Before submitting a change, run:

```powershell
dotnet build NInferEZ.Manager.slnx -c Release
dotnet run --project tests\NInferManager.Backend.Tests\NInferManager.Backend.Tests.csproj -c Release
```

GPU execution is never implied by a successful host-only build. New GPU packages remain Community
Preview until tested on representative hardware and models.
