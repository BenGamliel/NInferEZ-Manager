# NInferEZ Manager 0.0.1 — completion plan

Date: 2026-09-30. This is the active product plan for the isolated source tree containing this document. Product, assembly, installer and user-facing version remain **0.0.1**. The directory name is an organizational boundary, not a product name or runtime version.

## 1. Outcome and scope

Deliver a coherent Windows inference manager, not a collection of exposed CLI flags. A non-technical user should be able to obtain a matching engine, put a model in `Models` or link it, copy the API endpoint and use it. A technical user should retain exact profiles and useful diagnostics. The application must never trade numerical quality, quantization or engine performance silently for a tidier interface.

The retained baseline under `TEMP` is read-only for this work. The lightweight NInferEZ application, old NInfer Manager, model weights, conversion sources, Next research and existing engine builds are out of the implementation scope. Findings about those products in the code-review report are recorded, not silently applied to them.

Three deliverables come from the same application source and version:

1. A clean per-user Windows installer.
2. A clean Portable ZIP and extracted folder.
3. A separate local ready-to-test Portable containing the already verified matching engine and model links where appropriate. Model weights are never copied into it.

Public packages contain no user data, API credentials, absolute machine paths, model weights or local test settings. The local test copy is not a public upload artifact.

## 2. Audience and jobs

| Audience | Main job | Successful result |
|---|---|---|
| Everyday operator | Run a local model without learning NInfer arguments | One visible next action, readable status, a usable API URL and helpful recovery |
| Power user | Control context, KV and speculation while preserving a working profile | Exact saved profile, supported controls, visible effective session and reversible changes |
| API integrator | Connect another application reliably | All locally available model IDs, user-chosen aliases, structured errors and streaming |
| Community tester | Report a hardware or model issue safely | Honest Preview status, bounded logs and redacted diagnostics without prompts by default |

Not included: chat UI, arbitrary model marketplace, cloud accounts, Windows background service, plugins, new CUDA kernels, model conversion, unsolicited firewall changes, or automatic public publishing.

## 3. Team responsibilities and boundaries

- Team lead owns integration, update transaction, source isolation, packaging, verification and final handoff.
- Backend owns lifecycle, public and private HTTP contracts, settings/library persistence, downloads, bounded telemetry and host tests.
- Frontend owns WinUI presentation, state-dependent actions, layout, navigation, forms, accessibility and user-facing recovery. It cannot invent backend state or alter the inference profile to fix a visual problem.
- Product/acceptance owns this plan, acceptance matrix, end-user/security/contributor documentation, static release checks and independent workflow audit.

Changes to shared contracts are coordinated between backend and frontend. Installer edits are coordinated with the lead and the storage migration implementation. Agents do not make independent copies of models, replace engines, publish releases or launch GPU tests.

## 4. Architecture to keep and improve

Keep native WinUI, the separate ASP.NET/Kestrel backend, and NInferEZ Engine as an independent immutable package. No framework rewrite is justified by the review. Refactor the risky boundaries, particularly lifecycle, identity, settings writes and portable update, into explicit testable components.

### 4.1 Session versus configuration

Saved configuration represents what the user wants next. A loaded session represents what is actually running: generation/instance ID, owned PID, executable package identity, actual backend port, model identity, alias accepted by the running engine, immutable effective profile and private credential.

The UI must not show a newly saved context or engine as already running. Launch-affecting changes are pending until coordinated reload; harmless preferences apply immediately. Repeated load returns the existing operation rather than another process. Exit events from old generations cannot reset a new session.

One lifecycle coordinator must serialize operations that mutate the loaded engine and leases held by inference. Manual unload, timer unload, model switch, engine activation, alias changes and update shutdown use the same boundary. A request in progress must finish or be explicitly cancelled by the chosen operation policy; a race is not an acceptable policy.

### 4.2 Lazy API

The public API starts with the app on port 8173, without a model or CUDA allocation. `/health` and `/v1/models` are metadata-only. A request names any available local model by its default ID or user-defined alias; the app selects and loads that model lazily. Loading state survives UI navigation and tray hiding. Default unload is three idle minutes, with immediate, timed and keep-loaded options.

Keep engine concurrency at one unless a supported explicit profile selects otherwise. If the proxy rejects concurrent requests, disclose that policy and return a structured 429 with retry guidance. Do not claim a queue is implemented merely because the engine has a queue flag. Stored-response/session routes must not trigger an unrelated new model load.

JSON routing must reject malformed/non-object bodies as 400, unknown model IDs as 404, body-limit violations as 413, incompatible/missing engines as actionable errors and transient unavailable inference as 503. A bounded request-size policy is shared across proxy and engine. Preserve SSE cancellation and bytes; do not use regex to parse JSON.

### 4.3 Trust boundaries

Private control is authenticated and loopback-only. UI checks identity and uses the token belonging to its app/data root, not merely any healthy service on a familiar port. Engine readiness uses the running session's credential and distinguishes process-alive from model-ready.

LAN binding remains off by default. Enabling it requires an API key or a clearly acknowledged unsafe exception if intentionally supported. Binding `0.0.0.0` exposes all listening interfaces, including VPN adapters; it is not a firewall allowlist. No firewall or Tailscale changes occur automatically. Authentication is checked before expensive model loading.

Remote feeds are bounded data, never executable settings. Engine/update packages require the official HTTPS repository, safe identifiers, expected asset, size/hash verification and safe extraction. A user-supplied matching hash does not establish source trust. Manifest signatures are not claimed until key provisioning and verification genuinely exist.

## 5. UX information architecture

Retain the six established destinations: Overview, Models, Requests, Logs, Engine, Settings. Engine management stays on Engine; no Engine tab is inserted into Settings. Simple controls appear first and technical controls are progressively disclosed.

### Overview

Answer: is the API listening, what is selected, what is actually loaded, is a request active and is action required? Primary model action changes in place between Load, Cancel loading, Unload and Retry. Copy endpoint and model ID work before model load. Secondary telemetry cannot dominate setup or error recovery.

### Models

The upper local library lists `Models` artifacts and linked files. The lower Recommendations section shows the two curated existing model families, not a new general marketplace. Search/filter, select, user alias and profile editing must be accessible without unrelated dialogs.

Available files appear in `/v1/models`; missing linked files remain visible as Missing with Relocate/Remove link, but are not advertised as runnable. Managed versus linked origin is explicit. Removing a link never deletes the external file. Removing an app-managed file is a distinct confirmed action and respects its current use.

Aliases are optional and entered by the user; the application does not invent short names. Validate collisions, empty/whitespace policy and unsafe control characters. A model's API name is shown/copyable where relevant.

Recommended card primary action is Download, Pause/Cancel, Resume, Verify, Installed or Use according to real state. Show actual size, source link, format, vision and compatibility only when known. A missing vision component means the app cannot truthfully claim vision just because a model family supports it. DFlash2 requires the actual model component and compatible engine, not its filename.

### Requests and Logs

Bound recent history. Explain whether metrics cover the current engine session or retained log window. Unknown values use an em dash, not zero. Session identity prevents request-ID collisions after restart. Never retain prompt/response contents by default. Visible page refresh is single-flight; hidden pages and tray do not render or scan logs repeatedly. Logs offer search, pause/autoscroll, copy/export and open folder, without being the only way to understand an error.

### Engine

Show selected/installed/running package separately and a friendly hardware family (`RTX 5000 Series`, not `5090 only`). Recommendation is based on real compute capability when obtainable, with unknown hardware explicitly unknown. Build `sm120a` maps to Blackwell 12.0; `sm86` and `sm89` represent different packages, not different application versions.

Display installation/verification/activation progress inline, a single primary action and cancel where supported. Downloading may occur during inference; activation must wait for a safe lifecycle boundary. Keep the last-known-good package until a meaningful successful load. A host-only manifest check alone does not qualify it on a GPU.

First run without an engine cannot be dismissed as a completed setup. Recommend a compatible package, allow manual valid choice and show actual transfer state. Existing usable local engine enables offline startup. Failure leaves Retry and useful details instead of restarting setup endlessly.

### Settings

No global Save Changes. Preferences autosave atomically with small feedback; text/number commits use focus/Enter/debounce. Model profile has its own Save/Restore action. API bind/port/key changes have a deliberate Apply/restart transaction. Invalid input does not discard the last good value. A stale UI snapshot cannot overwrite aliases or profiles saved elsewhere.

Group appearance/startup, memory lifecycle, local API and maintenance on a responsive grid. Advanced fields show units, dependencies and restart consequences. Unsupported/no-op fields are hidden or explicitly disabled with reason, not editable placeholders. Maintain light/dark/system contrast and default clean status surfaces.

### Navigation and notification rules

Logo occupies its own padded upper-left zone. Collapse/expand control stays inside the rail, works both ways, and does not float over page content. Compact rail retains recognizable icons and tooltips; lower status remains visible. Secondary `By` publisher mark is not the app logo.

Notifications expire after ten seconds; hover retains the current notice, and leaving after its deadline closes it. Polling the same operation must not continuously reset the deadline. Progress lives inline rather than being emitted as repeated success/error notifications. Keyboard navigation, focus, meaningful automation names and DPI scaling are acceptance items, not presumed from XAML compilation.

## 6. Model identity and profile quality contract

Artifact identity must not be inferred from equal file size. Separate library key/path, artifact identity and user alias. Prefer the engine's GPU-free inspector, validate output and use conservative Unknown when unavailable. Never hash tens of GB on every view refresh. Cache metadata on size/mtime/identity and invalidate appropriately; exact identity verification remains an explicit trust policy.

Profile modes distinguish exact/custom intent from automatic recommendations. An imported settings file is versioned, data-only and validated; it cannot select an arbitrary executable, run commands or inject environment variables. A sidecar profile can travel with the model, including engine requirements and artifact identity. Unsupported profiles fail clearly rather than silently changing KV/speculation.

The current local IQ3 DFlash2 profile and artifact must be preserved from actual working data; the older plan's IQ3 MTP3 example is historical, not permission to overwrite it. The NVFP4 model retains its known vision/speculative components. Merely adding a recommended download entry does not prove it is the same bytes as the user's latest DFlash2 artifact.

Auto context uses the chosen device, actual available memory, artifact limits and workspace reserve. It returns No fit when no configuration fits; it cannot fabricate a 32K fallback or exceed the model limit. Show estimate, not a guaranteed maximum. Auto may lower context transparently but cannot lower weight/KV quality silently. Exact profile does not mutate on load. New recommendations are opt-in for an existing custom profile.

An unfamiliar artifact without calibrated costs uses a clearly labeled explicit starter profile,
not Auto. The starter context may begin at 32K but must respect an inspected lower model limit;
it is not a promise of memory fit. Users can edit context in basic settings. Metadata-declared SHA
and a full on-disk verification are separate states. Import/export preserves all typed Manager
profile values, including an explicitly chosen KV capacity and optional local resource paths;
sharing those paths remains deliberate and visible.

## 7. Storage, download and updates

### Layout and migration

Portable runtime data stays with the Portable root. Installed mutable data belongs under the documented per-user data root. If migration from legacy app-root data is implemented, back up first, preserve aliases/profiles/model references, never copy weight files, and avoid both source and destination being live writers. An unimplemented storage migration must be disclosed, not described as complete.

Models and engines have canonical storage. Development, Installer and clean Portable do not duplicate model weights. Immutable app binaries may be hardlinked for local staging; user Data, logs, pointers and settings must never be hardlinked. Hardlink sizes are logical sizes, not sums of physical disk usage.

### Downloads

Use bounded streaming and a `.part` file with artifact identity. Close writer before verification and close reader before rename on Windows. Verify expected size and SHA before committing Installed. Validate Range/Content-Range, server restart without Range, offset mismatch and completed partial; cancellation retains a resumable partial only when safe. Display phases including verification, speed and real ETA when calculable. Disk-full/permissions/network/hash errors are recoverable and preserve a good existing artifact.

### Feeds

Model catalog, engine channel and app releases remain separate. Refresh after UI/API startup, on configured intervals and explicit command; use valid cache/snapshot offline and bounded response size. Malformed/newer-schema remote data does not overwrite valid cache. Preserve seen IDs; NEW applies to new artifacts, while profile changes are separate notices. ETag/backoff are optimizations, not reasons to block offline startup.

### App update transaction

Download from the official release and select asset server-side. Validate entire staged layout before shutdown. Wait for owned app/backend PIDs to exit; a timeout is failure, not permission to delete locked files. Replacement affects app-owned code/resources only, never user files. Journal/backup must allow recovery after partial copy. New app health/migration acknowledgment proves commit; failed launch restores the prior application. Delete the temporary prior version only after success, as the user's single-active-version requirement intends.

Installed upgrades use the installer and preserve user data. Portable uses a separate helper while code files are unlocked. An app update does not silently update engine or weights. No forced update. Checking and applying are separate user actions.

## 8. Resource and inference constraints

- No real model load or GPU test during this implementation without explicit user approval.
- No model-weight hash, transfer, conversion or engine build merely to run ordinary host regression tests.
- UI hidden/tray stops high-frequency polling; visible refresh is single-flight and page-specific.
- Telemetry failures have timeout and negative cache; never block launch indefinitely.
- Logs/metrics are bounded and incremental. Error logging must survive a display or telemetry failure.
- Network failures/timeouts have cancellation and visible outcomes; shutdown awaits owned work.
- Source/binary layout remains professional and simple. Do not introduce abstraction/plugin/job frameworks without demonstrated need.
- Never claim a throughput increase or unchanged inference output from host-only compilation. Same binary/effective preset is necessary but GPU comparison is the actual evidence.

## 9. Engine-change decision

Current review supports **no engine or CUDA changes**. The required improvements are app lifecycle, HTTP adapter, metadata, IO, updates and UX. Keep published architecture-specific builds and attribution unchanged. If inspection reveals a genuinely missing engine contract/feature, document exact incompatibility and ask the user before creating another runtime build or changing kernel/numerical behavior.

Hardware compatibility wording: Installed/downloadable architecture support is not hardware qualification. Build/host-tested `sm86`, `sm89` and `sm120a` can be offered as compatible/Community Preview according to manifest. Only evidence from a specific GPU+artifact+profile can justify Tested/Stable labels. Available VRAM alone does not imply support for every model format.

## 10. Execution sequence and acceptance

1. Isolate source; inventory inherited behavior and 47 review findings; freeze version and baseline.
2. Stabilize trust boundaries, lifecycle/readiness/shutdown/settings and file IO using fake HTTP/engine fixtures.
3. Integrate coherent state-driven UI, metadata/library/profile workflows and bounded logs/metrics.
4. Implement transactional update and storage/privacy packaging checks; audit against acceptance matrix.
5. Run solution build and lightweight host tests; record actual results and unresolved items.
6. Produce three packages from the same tested source, validate layout/hash/privacy and verify no duplicated weights.
7. Provide ready-to-test Portable and clear unverified UI/GPU items. Stop before expensive tests and ask the user if required.

## 11. Release gate and truthful status

`ACCEPTANCE-MATRIX.md` records source, automated host, UI and hardware evidence separately. A product is not declared fully hardware-qualified because its code compiles. A build can be handed off for user UI testing after critical host workflows and packaging pass, with remaining manual/GPU gates explicit.

Blocking defects include auth bypass, arbitrary executable download, destructive update without recovery, data loss, duplicate owned engine processes, broken aliases/API/readiness, failed clean launch and a UI action that claims success while its backend failed. Cosmetic refinement does not waive these gates.

The previous `NEXT-VERSION-0.0.1-PLAN.md` and code-review report remain historical inputs; their completed claims refer to the retained baseline, not this isolated implementation. This document and the acceptance matrix define completion evidence for this source tree. No automatic deletion of the baseline or unrelated research is authorized.
