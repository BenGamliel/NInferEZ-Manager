# NInferEZ Manager

NInferEZ Manager is the native Windows control center for
[NInferEZ Engine](https://github.com/BenGamliel/NInferEZ-Engine). It makes local NInfer models
accessible to non-technical users without hiding advanced controls from users who need them.

The public OpenAI-compatible API starts with the application on port `8173`.
The selected model remains outside VRAM until the first inference request, and
is unloaded after three idle minutes by default. The interface exposes simple
defaults first while keeping engine profiles and advanced controls available.

## Layout

```text
src/                    Native WinUI application, backend and shared contracts
tests/                  Backend contract and command-generation tests
design/prototype/       Approved interactive visual reference
docs/                   Product, release and third-party documentation
installer/              Per-user Windows installer definition
scripts/                Reproducible packaging and release auditing
feeds/                  Data-only remote model recommendations
```

## Build and test

```powershell
dotnet build NInferEZ.Manager.slnx -c Release
dotnet run --project tests\NInferManager.Backend.Tests\NInferManager.Backend.Tests.csproj -c Release
```

The test program does not load a model or allocate GPU memory.

## Package

Pass an extracted, verified NInferEZ Engine directory for a release-candidate package. It must contain
`ninfer-serve.exe` and `engine-manifest.json`.

```powershell
.\scripts\build.ps1 -BuildId dev-001 -EngineSource C:\path\to\extracted-engine
```

The script creates an installer, a clean Portable ZIP/folder, and a separate
ready-to-test Portable folder. Every staging tree is checked for settings,
logs, model weights, debug symbols, local paths and token-shaped secrets.

## Status

Product version `0.0.1` supports independently managed `sm86`, `sm89` and `sm120a` engine packages.
RTX 5000 Series cards, including RTX 5080, use the Blackwell `sm120a` package; unqualified models are
shown as Community Preview. No model weights or engine binaries are stored in this repository.

Ordinary preferences save automatically. Model profiles and API restarts remain explicit operations.
See `docs/RELEASE-NOTES-0.0.1.md` and the complete product plan in
`docs/NEXT-VERSION-0.0.1-PLAN.md`.

<p><small>By</small><br><img src="assets/2beng2.png" alt="2beng2" width="120"></p>
