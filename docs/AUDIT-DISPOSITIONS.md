# Disposition of the 47 historical review findings

Date: 2026-09-30. Scope is this Manager source tree only. This maps every numbered finding from
`CODE-REVIEW-2026-09-30.he.md`; it does not claim that the lightweight product, older Manager,
engine or Next research were modified.

**Source implemented** means the corrective path was observed in current source, not necessarily
tested. **Partial** means the exact remaining limitation is retained. **Preserved/deferred** means
no speculative rewrite or costly measurement was performed. **Outside scope** means the cited
component belongs to a retained independent product. Automated evidence is maintained separately
in `ACCEPTANCE-MATRIX.md`; UI and GPU qualification cannot be inferred from this table.

## Runtime efficiency and reliability

| Finding | Disposition and actual boundary |
|---|---|
| W01 Authenticated readiness | Source implemented and fake-engine readiness regression observed: readiness uses the loaded session credential. No real native load is implied. |
| W02 Windows download handles | Source implemented: model/update writes and verification reads use closed scopes before commit. Windows fixture tests are the proof of rename/resume behavior. |
| W03 Saved settings versus running session | Source implemented: immutable loaded-session port/key/profile and revisioned narrow settings changes. Source audit also requested commit-after-file-write and removal of bypass mutations. |
| W04 Shared lifecycle coordination | Source implemented: inference and destructive control operations use the same lifecycle boundary; idle checks use nonblocking acquisition. Race tests are still distinct evidence. |
| W05 Await owned process exit | Source implemented: shutdown waits for owned process exit and scopes late events by generation. No claim of instantaneous VRAM release on every driver. |
| W06 Auto context correctness | Source implemented: selected device, no-fit error, no mutation of saved exact profile, calibrated-profile guard. Unknown model starter configuration must be explicitly uncalibrated. Coefficients remain estimates until real hardware measurement. |
| W07 Full JSON parsing before forwarding | Partial: body limits/buffering bounds and error handling added; full JSON parsing remains. A streaming parser is deferred until memory/TTFT comparison demonstrates value and preserves complete JSON validation. No tok/s improvement is claimed from this alone. |
| W08 Proxy versus engine queue | Source implemented: bounded proxy waiting with timeout/cancellation and Retry-After; concurrency stays one. Actual queue/profile policy must be shown accurately. |
| W09 Repeated log/metrics scans | Source implemented: bounded in-memory tail, one buffered writer, timed flush and incremental metrics; writer disposal failure is guarded. Full disk/permission failure injection and long-run memory measurements remain separate gates. |
| W10 Metrics history identity | Source implemented: session accumulator and long total counters. UI still needs to identify its history window. This is not full persistent request analytics. |
| W11 Overlapping UI polling/notices | Source implemented: single-flight refresh and operation/stage notification tracking; hidden/tray work reduced. Ten-second hover behavior and visible transitions require UI testing. |
| W12 GPU subprocess timeout/backoff | Source implemented: bounded telemetry subprocess, negative cache and multiple-device parsing. Hardware recommendation is separate; generic family words must not stand in for actual compute capability. |
| W13 Lightweight startup/stop | Outside scope: applies to NInferEZ.Core in the retained lightweight application, not this Manager implementation. |
| W14 Lightweight handwritten HTTP | Outside scope: no replacement of the independent lightweight proxy. Manager retains Kestrel. |
| W15 Lightweight repeated full hashes | Outside scope for that application. Manager must not newly introduce full weight hashes into ordinary startup/refresh; preset metadata hashes are not claimed as byte verification. |
| W16 Working-set trim | Automatic trimming is not used as proof of efficiency. Manual helper may remain for explicit maintenance; measuring private bytes/page faults is deferred, not a justification for forced GC during inference. |

## Storage

| Finding | Disposition and actual boundary |
|---|---|
| S01 Existing hardlinks | Preserved principle: only immutable package binaries may be linked. Mutable settings/logs/engine pointers and model files are not shared through mutable hardlinks. Logical size is not physical space freed. |
| S02 Repeated payload copies | Packaging lead owns canonical staging/shared immutable payload. Final package evidence is required; merely editing the script does not establish physical savings. |
| S03 Weight duplicates in historical caches | Outside scope and preserved: retained source/model/Next materials are not deleted. This task never duplicates weight files into app artifacts. |
| S04 Engine build-cache relocation | Outside scope: no engine recompilation, cache deletion or reconfiguration. Existing runtime artifacts remain unchanged. |
| S05 Installed/partial retention | Partial: cancelled engine partials and update staging have explicit handling; prior app recovery is retained until successful commit. General engine/cache storage management is not claimed as fully measured or globally cleaned. |
| S06 Different self-contained frameworks | Preserved/deferred: framework/runtime layout kept to avoid destabilizing WinUI. Any change needs compatibility, startup and actual package-size measurement, not a speculative rewrite. |
| S07 Aggressive binary compression | Intentionally not performed. No UPX or removal of CUDA dependencies based only on file size; architecture-specific immutable engine packages preserved. |

## Security, contracts and maintainability

| Finding | Disposition and actual boundary |
|---|---|
| G01 Control API trust | Source implemented: private random control-session token, loopback backend and authenticated identity check. App update choice is backend-verified rather than a free-form client URL/hash. |
| G02 Network/CORS | Source implemented and credential-transition fixture observed: LAN remains off, enabling requires a key and the listener captures its credential before binding. CORS is an explicit setting, not a firewall boundary. No VPN/firewall mutation performed. |
| G03 Transactional Portable update | Source implemented: validated staging, app-owned components, PID wait, backup/journal, acknowledgment, rollback and retry cleanup. Tiny archive/failure fixtures are required; a real release/install transaction remains a separate manual gate. |
| G04 Engine feed identifiers/runtime validity | Source implemented: architecture/version/path containment, validated pointer grammar and serve/inspector/manifest checks before staged replacement. Complete archive/feed failure-injection coverage remains pending; a manifest alone is not hardware qualification. |
| G05 Engine install versus inference | Source implemented: commit/activation callback acquires lifecycle boundary; transfer can be separate from final switch. Need tests distinguishing selected versus actually running package. |
| G06 Model identity/capabilities | Source implemented and restart fixtures observed: removes arbitrary same-size identity matching, adds GPU-free inspection and bounded atomic metadata persistence. Cached capabilities require matching model path/size/mtime and inspector stamp; relink resets verified identity even for equal size/mtime. Metadata does not authenticate model bytes; explicit verification remains separate. |
| G07 Managed versus linked origin | Source implemented: discovery no longer becomes a Link command; origin and Missing state are explicit. External unlink cannot delete the external file. |
| G08 Travelling preset contract | Source implemented and roundtrip/restart fixtures observed: versioned data-only `.ninferez.json` import/export, strict typed Manager-profile supplement, validated mode/capacity and unverified identity disclosure. ExpectedSHA/status survive restart; malformed adjacent presets remain guarded even after fallback profile creation, with only deliberate unchanged-sidecar custom overrides retained. Revalidation never overwrites saved numerical settings. Custom local resource paths remain explicit user responsibilities; no cross-product shared-library claim. |
| G09 Settings schema/recovery | Source implemented: schema/revision, validation, atomic candidate commit and recovery backup/notice. Legacy links/data require non-destructive migration fixtures. |
| G10 Public API route coverage | Source implemented: route classification, nonobject/malformed input handling, stored-response behavior and canonical alias policy. Supported OpenAI-compatible routes are explicit; not every possible OpenAI endpoint is claimed. |
| G11 Listener restart rollback | Source implemented: actual bind attempts, old listener restoration on failure, and deliberate Apply path. Persisted versus listening state must remain consistent on failure. |
| G12 Diagnostic redaction | Source implemented and small regression observed: removes known API credentials, aliases, profile identity keys and resource paths; sanitizes CLI API keys, Bearer tokens and drive paths. Guide and diagnostic summary require reviewing arbitrary free-text before sharing; absolute anonymity is never guaranteed. |
| G13 UI responsibilities/reflection editor | Source implemented: explicit setting descriptors with labels, limits, help/groups and supported scalar types; state-driven layout and artifact-aware acceleration choices, with pure helper regressions. Visual quality/interaction remains a manual gate. Broader MVVM/framework rewrite intentionally not performed. |
| G14 Owned tasks/cancellation | Source implemented across central workflows: operation ownership, cancel, frozen final elapsed and bounded shutdown. Independent lifetime audit still necessary; no generic job framework introduced. |
| G15 SemVer/channels/feed optimization | Partial: existing official stable-release path remains. Full preview channel/SemVer and ETag policy requires explicit implementation evidence; not inferred from a version constant or cache file. Product 0.0.1 remains fixed. |
| G16 Build provenance | Packaging lead implements source digest/build metadata where no Git checkout exists. Hashes identify binaries; source digest identifies the copied source. Final artifact audit is the proof. |
| G17 Engine contributor instructions | Outside scope: retained engine repository is not edited by the Manager completion task. No CUDA build is justified. |

## Dead code and historical boundaries

| Finding | Disposition and actual boundary |
|---|---|
| D01 Unused update field | Source cleanup observed: `_availableUpdate` no longer present in UI source. No material performance gain attributed to removing one field. |
| D02 No-op cache marker setting | Source implemented: descriptor and launch argument for supported `--max-cache-markers-per-request`. User control not deleted blindly. |
| D03 Unused UI import adapter/properties | Unused UI adapter removed where not needed; backend import/verification contract preserved. Runtime serialization/XAML properties are not deleted solely on textual reference counts. |
| D04 New/old Manager duplication | Outside scope: old product remains retained/frozen; no forced merger or cross-product shared-state refactor. |
| D05 Lightweight Setup project | Outside scope and preserved. No deletion based on a mistaken assumption that a Portable first-run setup makes installer/uninstaller code dead. |
| D06 Next/research | Outside scope and preserved as required. No research code is placed in this Manager startup graph. |
| D07 Converter/mathematical data path | Preserved: weights, conversion sources and engine math unchanged. UI cleanup is not permission to requantize or remove toolchain support. |

## Final interpretation

The central Manager reliability/security defects are app-only changes; no engine kernel rewrite is
justified. Remaining measured-performance experiments, historical cleanup and independent-product
findings are not hidden as completed work. Check the acceptance matrix and package test results for
the final observed state; this source-disposition map is not a universal hardware-support certificate.
