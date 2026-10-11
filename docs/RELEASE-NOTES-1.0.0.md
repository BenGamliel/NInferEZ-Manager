# NInferEZ Manager 1.0.0

Release packaging dated 2026-10-11. Includes the approved settings fixes:
hover-only circular help, compact model quantization labels, inactive vision flag omission,
managed advanced dependencies, retained manual backups, and actionable profile errors.
No loading, CUDA graph, warmup, unload timing, inference engine or model-weight changes were
made while preparing this release. Public Installer and Portable contain no private settings,
model links or weights; the separate local Test Portable preserves existing model links.

Native Windows model and API management with a simple-first interface, independent engine
packages, local/linked model library, optional user-defined API names, curated recommendations,
profiles, request metrics, logs, tray and diagnostics.

The Converter page includes Guided wizard and Manual setup, powered by the existing
independent converter backend and an app-local CPU runtime. It discovers required files,
compares supported conversion options, presents actual per-weight plans and checks the
selected plan before conversion. Manual resources, recipes, companion components and
per-weight assignments are available without a separate converter window. Existing
NInfer files offer the supported lossless repack route. CPU conversion is included;
optional inference verification needs a compatible engine and available GPU memory.
No model weights or external model links are included in the public packages.

## Behavior and safeguards

- Optional managed advanced settings coordinate basic context, cache quality and acceleration choices; the last manual setup is backed up and can be restored.
- Categorized advanced controls use question-mark help and grey out inactive dependencies. Disabled acceleration no longer sends stale MTP, LM-head or N-gram flags.
- Managed automatic cache fitting uses the engine's post-weight VRAM budget without changing KV precision. Actual startup errors are retained instead of usage-text footers.

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
separately. No GPU inference was rerun during release packaging.

Digital-signature/code-signing assurances are not claimed where only HTTPS and SHA verification
exist. Review diagnostics before sharing. The [user guide](USER-GUIDE.md) explains first run,
API integration, profiles, memory policies and failure recovery.
