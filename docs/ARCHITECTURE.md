# Architecture

NInferEZ Manager separates the responsive desktop shell from long-running and privileged runtime
work. The WinUI process renders the interface and tray icon. A loopback-only ASP.NET backend owns
settings, downloads, engine processes, updates and the OpenAI-compatible proxy.

## Runtime flow

1. The manager API starts immediately on port 8173 by default.
2. `GET /v1/models` reports the selected model without allocating GPU memory.
3. The first inference request starts one shared engine-load operation.
4. UI or client cancellation stops waiting for that operation but does not implicitly kill it.
5. Explicit Cancel or Unload owns process cancellation.
6. The idle policy releases VRAM after three minutes by default.

## Independent engine packages

The application reads the official NInferEZ Engine channel and recommends `sm86`, `sm89` or
`sm120a` from the detected NVIDIA GPU family. Downloads are size-checked, SHA-256 verified, safely
extracted to versioned directories and activated through an atomic pointer. The application never
switches engine packages while a model is loaded.

RTX 5080 and other RTX 5000 Series cards map to the Blackwell `sm120a` package. Compatibility based
on architecture is distinct from completed model-level qualification, so untested combinations are
shown as Community Preview.

## Remote data

Model recommendations are a versioned, data-only JSON feed. It may update model metadata and tested
profile values, but cannot select executables, environment variables or arbitrary engine arguments.
The embedded catalog remains the offline fallback.

Application updates use only GitHub Releases from the official NInferEZ Manager repository. Engine,
model, settings and log directories are preserved across application updates.
