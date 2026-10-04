# Contributing

NInferEZ Manager is a Windows desktop client, not a CUDA implementation. Keep the application,
engine and artifact boundaries explicit. Preserve user aliases, profile intent and localhost-first
behavior. Do not commit weights, engine binaries, runtime tokens, logs, private settings, dumps,
machine-specific paths or the local ready-to-test copy.

## Product invariants

- Product version remains `0.0.1` for this development line; use build metadata rather than changing branding for local iterations.
- A saved setting is not necessarily the running engine setting. Session identity and effective profile must remain truthful.
- Lazy API metadata must not allocate GPU resources; telemetry failures must not block inference.
- Model linking never copies or implicitly deletes the user's external artifact.
- Custom/exact profiles are not silently downgraded to save VRAM or satisfy a new recommendation.
- Remote data cannot select arbitrary commands or executables. Download/update verification is tested with small fixtures.
- Keep code cohesive and readable. A framework rewrite or generalized plugin system needs demonstrated product value.

## Fast host regression

```powershell
.\scripts\verify-host.ps1
.\scripts\product-check.ps1
```

Use the project's pinned SDK configuration. Tests use local HTTP/fake-engine fixtures and tiny
artifacts; do not replace them with a 27B-model load. Add regressions for cancellation, state races,
Windows file sharing, stale settings, malformed inputs and failure recovery, not only happy paths.
The host verifier builds and runs the backend, updater and authenticated-control test projects
serially, and saves observed results under `artifacts/verification`. Use `-SkipRestore` only when
dependencies have already been restored. It does not launch the desktop UI or native inference.

Source review, host tests, package audits, UI inspection and real GPU inference are separate evidence
levels in [ACCEPTANCE-MATRIX.md](docs/ACCEPTANCE-MATRIX.md). Update it with observed commands/results,
not inferred success. Fake engine readiness does not prove native model compatibility.

## Packaging and heavy tests

Build one canonical payload and derive Installer, clean Portable and local test Portable. Public
packages must pass release/privacy checks. Keep mutable Data out of hardlinks. Record source
digest/commit status and artifact hashes; a missing Git checkout is not a reason to invent a commit.

GPU runs, long contexts, full model hashing, large transfers and CUDA builds require explicit resource
approval in this development environment. Existing engine bytes and profiles stay unchanged unless
an actual unsupported contract/feature requires an approved separate change. Untested architectures
or model/card combinations remain Community Preview.
