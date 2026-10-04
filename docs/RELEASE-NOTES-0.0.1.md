# NInferEZ Manager 0.0.1

Native Windows model and API management with a simple-first interface, independent engine
packages, local/linked model library, optional user-defined API names, curated recommendations,
profiles, request metrics, logs, tray and diagnostics.

## Behavior and safeguards

- API starts with the application, normally on port 8173. Models load lazily and unload after three idle minutes by default.
- Available models are listed without loading them; linking files does not copy weights.
- Engine/model loading is an explicit operation with phase, elapsed time and cancellation, independent of a short UI request timeout.
- Saved configuration is distinct from the effective loaded session. Ordinary preferences autosave; profiles and API restarts have local explicit actions.
- Private control authentication, validation, download staging/hash verification and update recovery protect sensitive management workflows.
- Model and engine maintenance remains state-dependent, while advanced settings do not dominate the default experience.
- Clean Installer and Portable exclude weights and personal settings. The local ready-to-test Portable is a separate private QA artifact.

## Compatibility and validation

`sm86`, `sm89` and Blackwell `sm120a` are separate architecture packages. RTX 5080 maps to the
Blackwell package, but matching architecture is not the same as model-level qualification.
Untested combinations remain Community Preview. Native NVFP4 support is not implied for older
targets; artifact components, driver and memory budget also determine compatibility.

Host-only build/regression checks validate application workflows without running a real model.
They do not prove vision correctness, long-context retrieval, inference quality, VRAM usage or
throughput on untested hardware. The evidence accompanying the build records these levels
separately. GPU execution requires explicit approval during development.

Digital-signature/code-signing assurances are not claimed where only HTTPS and SHA verification
exist. Review diagnostics before sharing. The [user guide](USER-GUIDE.md) explains first run,
API integration, profiles, memory policies and failure recovery.
