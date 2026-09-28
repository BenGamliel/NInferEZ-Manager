# Feature inventory and reuse plan

This inventory is based on the current NInferEZ source and the existing full
NInfer Manager backend. “Reuse” means extracting or adapting reviewed behavior,
not copying either previous UI.

| Capability | Lightweight NInferEZ | Existing full manager | NInferEZ Manager plan |
|---|---:|---:|---|
| Engine discovery and manifests | Yes | Pinned runtime | Shared core, capability-driven |
| First-run engine installer | Yes | Bundled runtime | Manager onboarding and Engine page |
| API available before model load | Yes | Proxy loads engine | Preserve NInferEZ lazy API behavior |
| Idle/immediate/keep-loaded policy | Yes | Timed unload | Unified lifecycle policy, 3 min default |
| Models-folder discovery | Yes | Installed catalog | Unified local library |
| Persistent external model links | Yes | Import paths | Unified library with missing-file state |
| User-defined API model name | Yes | Model IDs | Preserve exact optional alias behavior |
| Versioned model profiles | Yes | Rich per-model profile | New validated profile schema |
| Context/KV/speculation controls | Yes | Extensive | Contextual Advanced profile editor |
| GPU detection and VRAM status | Basic | Live status | Shared hardware service and live overview |
| Curated downloads and verification | Engine only | Models and updates | Full download center with hashes |
| Request-level metrics | No | Yes | Requests page and overview summary |
| Searchable in-app engine logs | File access | Basic tail view | Structured live log workspace |
| Diagnostics export | Basic logs | Yes | Redacted support bundle |
| Engine update/rollback | Download/activate | Update flow | Versioned packages, activation and rollback |
| Tray lifecycle | Yes | Yes | Unified tray state and actions |
| Installer and Portable | Yes | Yes | Independent packages, shared source commit |
| Light/dark/system themes | Light UI | Yes | System + dark baseline; both fully designed |

## Not carried forward as-is

- The lightweight single-page layout.
- The old manager’s large all-in-one XAML window.
- Polling and rendering data that is not visible.
- Catalog-first model selection as the only workflow.
- Any UI control that exists only because a CLI flag exists.

## First production slice after design approval

The first connected build will be deliberately vertical rather than broad:

1. Start UI and local core with no GPU allocation.
2. Show real API, engine, model and GPU state on Overview.
3. Discover the Models directory and external links.
4. Stream and filter real logs.
5. Load/unload one validated model through the existing safe lifecycle.

Downloads, updater and the full advanced editor follow once this slice is
stable and visually approved.
