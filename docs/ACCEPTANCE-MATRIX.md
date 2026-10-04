# NInferEZ Manager 0.0.1 — acceptance and evidence

Date: 2026-09-30. Scope: isolated source tree, unchanged product version. This matrix prevents static checks, fake engines and real GPU tests from being conflated.

Evidence codes: **S** source review; **H** automated host-only test with small fixtures; **P** packaged-layout/privacy/hash check; **U** interactive UI/clean install; **G** real GPU inference. `Pending` means no success is claimed. GPU tests require the user's explicit approval. Compiling a package does not establish hardware qualification.

## 1. Core acceptance matrix

| ID | Workflow and edge cases | Required proof | Status |
|---|---|---|---|
| A01 | Version is 0.0.1 in assemblies, ProductInfo and Installer; no folder label leaks into branding | S/P | Source static checks passed; packaged branding pending |
| A02 | Baseline under TEMP unchanged; no copied model weights | Inventory/P | Pending |
| A03 | UI/API start without engine model/GPU allocation; health and models metadata only | H/U | Public metadata host checks passed; UI startup pending |
| A04 | Default port 8173; locked conflict reported; fallback explicitly visible if unlocked | H/U | Default-port host check passed; conflict/fallback interaction pending |
| A05 | Single instance activates existing window, including tray; installer and Portable behavior coherent | S/U | Source path reviewed; actual repeated launch/tray pending |
| A06 | App titlebar, taskbar, tray, EXE and Installer use NInferEZ icon; By remains secondary | P/U | Pending |
| A07 | First-run engine choice mandatory, recommended by real hardware; offline usable engine accepted | H/U | Pending |
| A08 | Selected, installed and running engine shown separately; compatible architecture ≠ Tested | S/H/U | Source separation and bundled snapshot fixture reviewed; live switching UI pending |
| A09 | Engine feed identifiers, official source, size/hash, ZIP traversal and required runtime validated | H | Identifier/pointer/staging checks source-reviewed; complete feed/archive fixtures pending |
| A10 | Engine cancel cleans temporary files; activation serialized with lifecycle; rollback/pointer integrity | H/U | Lifecycle/token-aware commit source-reviewed; engine-install failure/cancel fixtures pending |
| A11 | Local Models discovery requires no model load or full weight hash; linked missing files retained | H/U | Discovery/no-copy/distinct-path host checks passed; Missing/Locate UI pending |
| A12 | Internal vs external removal accurate; external unlink never deletes user file; managed remove confirmed | H/U | Origins and unlink/remove source-reviewed; destructive UI workflow pending |
| A13 | Optional user alias persists; duplicate/invalid alias rejected; API publishes available IDs only | H/U | Persistence/API host checks passed; UI editing pending |
| A14 | Any advertised local model routable; actual identity never resolved by size alone | S/H; G smoke later | Canonical IDs/distinct paths host-checked; real multi-model inference pending |
| A15 | Artifact metadata/vision/DFlash2 reflect components, not filename; Unknown clearly represented | H/U; G later | Dynamic-choice and stamped-cache restart/invalidation host fixtures passed; UI/native artifacts pending |
| A16 | Model recommendations separate from local library; only intended curated entries; HF links correct | S/H/U | Two featured recommendations/source separation reviewed; online feed/UI pending |
| A17 | Download complete/resume/no-Range/mismatched Range/416/cancel/hash/size/rename pass on Windows | H | Complete/resume/no-Range/bad-Range/hash/rename fixtures passed; 416/cancel/size edges pending |
| A18 | Download disk-full/permission/network errors preserve prior good artifact and offer recovery | H/U | Pending |
| A19 | Profile save/restore/import/export validates schema and requirements; arbitrary code/env rejected | H/U | Typed roundtrip/invalid-sidecar/identity-export restart fixtures passed; import/export UI pending |
| A20 | Exact/custom profile not silently modified by Auto; effective running context/profile displayed | H/U | Frozen-session and restart-retained custom numerical profile fixtures passed; loaded/configured UI distinction source-reviewed |
| A21 | No-fit context and selected-device memory fixtures; never fabricated 32K tier beyond model cap | H; G calibration later | No-fit/multi-GPU parse host checks passed; starter cap source-reviewed; GPU calibration pending |
| A22 | Load operation idempotent and UI-independent; long fake readiness exceeds 15 sec with explicit cancel | H/U | Fake startup cancel/frozen elapsed passed; >15-sec readiness and timer UI pending |
| A23 | Readiness authenticates with running credential and detects crash/not-ready/ready correctly | H | Authenticated fake readiness passed; complete crash/not-ready permutations pending |
| A24 | Kill/unload awaits owned process exit; stale exit cannot alter replacement state; app crash containment | H | Fake owned exit/unload and actual-backend parent disappearance passed; all late-event/native-driver cases pending |
| A25 | Manual/idle/unload/rename/switch/update all coordinate with active inference; no timer race | H | Streaming versus manual unload and immediate-policy fixtures passed; full race matrix pending |
| A26 | Saved backend port/key/profile differ from frozen running session safely; no stale settings overwrite | H/U | Revision PATCH/stale conflict host checks passed; running-session mutation pending |
| A27 | Private control token+instance identity required; loopback binding only; unauthorized mutation rejected | H | Private-token/owned-session host checks passed |
| A28 | Public auth checked before model load; LAN off by default and deliberate protected enable/apply | H/U | Public-key/LAN captured-credential transition host checks passed; interactive LAN flow pending |
| A29 | Malformed/nonobject JSON400, unknown model404, size413, busy429, missing engine actionable503 | H | 400/404/413/missing-engine503 host checks passed; busy429 race pending |
| A30 | SSE streams without full response buffering, cancellation releases lease; stored-response routes no spurious load | H; G later | Fake SSE intact while unload waits; stored-session host check passed; disconnect/native latency pending |
| A31 | Preferences autosave; local Save profile and Apply API; invalid input cannot replace good settings | H/U | Typed PATCH/invalid/stale/disk-commit checks passed; interaction pending |
| A32 | Settings corruption backed up, schema migration preserves aliases/profile/model references, recovery visible | H | Settings persistence/stale ownership and corrupt/missing metadata-cache restart guard checks passed; settings corruption/migration permutations pending |
| A33 | Public listener reconfiguration rollback if bind fails; saved/actual endpoint never misleading | H/U | Validated candidate/key capture and rollback source-reviewed; bind/disk-failure fixture/UI pending |
| A34 | Feed offline/malformed/newer-schema fallback works; cache atomic/bounded; no continuous networking | H/U | Pending |
| A35 | App update chooses official asset and rejects arbitrary client URL/name/path/hash | H | Pending |
| A36 | Portable update stages full code, waits PIDs, preserves user data, journals and restores on partial failure | H | Archive/transaction fixtures passed; real process wait/update pending |
| A37 | Post-update acknowledgment precedes backup cleanup; failed startup returns old usable app | H/U | Confirmation/rollback fixtures passed; actual app startup integration pending |
| A38 | Installed upgrade/uninstall preserve user-owned data by default; removal prompt opt-in and correct scope | S/U | Source safe No/non-force closure reviewed; automatic-update relaunch source path added; interactive installer pending |
| A39 | Installed/Portable storage location documented and migration non-destructive/no weight copying | H/U | Source/docs reviewed, cross-volume legacy engine fallback retained; actual migration fixture/UI pending |
| A40 | Logs/Requests bounded incremental; errors retained; session IDs and long token counters | H/U | Bounded ring/writer/session source reviewed; long-run/IO fault/UI metrics pending |
| A41 | UI refresh single-flight/page-specific; tray stops hot polling; GPU monitor timeout/backoff | S/H/U | Source policy reviewed; hidden-window resource/UI validation pending |
| A42 | Notifications event-deduplicated,10 sec deadline, hover retention, no recurring poll reset | H/U | Source deadline/event tracking reviewed; actual hover/timing pending |
| A43 | Sidebar works compact/expanded, logo separate, footer unclipped; state actions no redundant buttons | U | Pending |
| A44 | Light/dark/system, focus/keyboard/automation names, scaling and narrow desktop responsive layout | S/U | Pending |
| A45 | Diagnostics redact credential/path fields and sensitive log patterns; no prompts by default | H/U | CLI/Bearer/drive-path fixture passed; source field redaction reviewed; free-text review still required |
| A46 | Installer/Clean Portable/Test Portable built same source; public packages no Data/secrets/weights | P | Pending |
| A47 | Engine bytes/Exact profile unchanged; no performance or quality regression versus direct binary | S/P; G later | Pending |
| A48 | User-facing errors are actionable, bounded and accurate; unsupported options do not falsely save | H/U | Pending |

## 2. Efficient test policy

Host tests use temporary tiny artifacts, local HTTP fixtures and fake engine processes. They may build/run managed code but cannot call CUDA inference, download a real model, hash the user's whole model collection or rebuild CUDA. Root records the commands, exit status and test-count output. A fake engine can exercise authentication/readiness/streaming without supporting actual generation.

The package checks inspect local code/layout and build metadata, not hardware. The clean-public check differs from the local-test check: local test configuration may contain this user's links and therefore is explicitly private, while no model weight copies are acceptable in either.

Resource-intensive checks stop and ask: real model cold/warm loads, inference/vision/DFLASH comparisons, 64K/256K/512K contexts, full model hashing, large downloads or engine CUDA compilation. GPU validation should compare the same binary and effective preset first; changing dtype/speculation at the same time obscures cause.

## 3. Release decisions

**Ready for user testing** requires build/host regression success, critical trust/lifecycle/download/update issues resolved or a failing action unavailable with explicit explanation, three coherent artifacts, no public personal-data contamination and no known broken control. U and G can remain pending only if handoff clearly states that they are pending.

**Qualified release** additionally requires interactive startup/navigation/settings/error/cancel/installer/upgrade checks, both intended model smoke tests and measured inference-overhead/quality validation on qualified hardware. Unavailable hardware stays Community Preview. Unsupported models are not described as universally compatible.

Do not change Pending to Passed based only on an agent's intent or patch. Record test names and observed output or precise source-only evidence in the execution section. A documented limitation is not an automated pass.

## 4. Execution evidence

Source/host verification is recorded below. Package assembly and interactive/GPU acceptance are
separate gates; inherited earlier build reports are not evidence for this source tree. Packaging
results belong beside the artifacts so recording them does not invalidate the source snapshot
hashed at packaging time.

### Product static check

Observed 2026-09-30: `scripts/product-check.ps1` in source-only mode returned exit 0, **13 passed checks**, zero findings. Checks cover product constants, required product documents/assets, per-user installer and non-destructive uninstall default. This does not test packaged files or an actual uninstall.

### Portable update fixtures

Team lead reported exit 0 for:

```powershell
dotnet run --project tests/NInferManager.Update.Tests -c Release -p:BuildInParallel=false -m:1
```

Twelve original small local fixture checks passed: successful replacement preserving user data and engine; partial-failure rollback; duplicate/concurrent confirmation; pending-recovery lock; checksum rejection; retry after RolledBack; unsafe ZIP traversal; safe retry after pre-journal interruption; duplicate archive paths; unexpected archive component; interrupted root-EXE recovery. The final run adds a thirteenth check rejecting a Portable target for the Installed-update helper. The success/confirmation fixtures include multiple assertions; thirteen checks do not mean thirteen real installed application upgrades.

No UI, GPU or model was started by these tests. Actual updated-app startup acknowledgment, parent process shutdown, Installed upgrade and interactive recovery remain separate pending gates.

### Final host verification

Observed `artifacts/verification/host-results.json` at 2026-09-30 20:18:42 UTC:

```powershell
.\scripts\verify-host.ps1 -SkipRestore
```

All three projects built in Release with zero warnings/errors and exited 0:

| Host suite | Passed checks | Run seconds |
|---|---:|---:|
| Backend contracts, Windows IO, restart and fake engine | 112 | 3.02 |
| Updater transaction and Installed-target rejection fixtures | 13 | 0.97 |
| Actual backend control/API and pure UI-choice fixtures | 45 | 3.62 |
| Total host checks | 170 | 7.61 |

The separate product static check returned 13 passes. The lead also observed the native WinUI
Release build with zero warnings/errors (8.59 seconds); this is compilation, not a visual test.

Backend cases include full/no-Range/resumed/bad-Range model downloads and hash rejection with tiny
fixtures; complete typed preset roundtrip; invalid preset mode and malformed sidecar isolation;
failed settings-disk commit preserving memory/revision; distinct same-name/same-size files;
no-fit context and multi-device telemetry parsing; CLI/Bearer/path diagnostic redaction;
authenticated fake-engine readiness; immutable loaded port/key/profile; intact fake SSE while
manual unload waits; owned process exit; API on/off; immediate-after-response unload; startup
cancellation and frozen terminal elapsed. Captured listener credentials were tested while stored
credentials still differed: missing credentials returned 401 and the new key returned 200.

Fourteen additional tiny restart checks cover linked ExpectedSHA/status persistence without a
full-model hash; usable preset export/import after restart; complete custom numerical-profile
retention; capability-cache reuse only with matching model path/size/mtime and inspector stamp;
invalidation when the artifact or inspector changes; verified-identity reset on same-size,
same-timestamp relink; and invalid adjacent preset guards after snapshot/fallback-profile creation,
restart, missing/corrupt metadata cache, intentional custom override and subsequent sidecar change.
The atomic bounded local cache persists metadata only. Adjacent sidecar validation does not
overwrite a saved numerical profile. A fingerprint reads only a tiny settings sidecar, not weights.

Control cases launch the actual managed backend with isolated temporary data and private token,
not a real inference model. They cover private unauthorized401 and owned PID/session identity;
public-key401; unloaded health/models; alias persistence; revisioned PATCH invalid/stale400/409;
typed profiles; Content-Length/chunked1MiB413; malformed400; unknown404; stored GET409 without a
session; API off/on; missing engine503; requested-model versus default-model body budget;
distinct external file paths/API IDs without copies; malformed adjacent preset at startup; and
clean backend exit when its UI parent disappears. Five pure helper checks cover inspected-only
acceleration choices, preserved unsupported imported choices, unknown metadata and Disabled.

No desktop GUI, CUDA inference or actual weights were started. A fake HTTP stream is not a native
generation/queue/memory-quality benchmark or proof of arbitrary model compatibility. The matrix
deliberately leaves unexercised 416, disk-full download, complete engine/archive races,
installer interaction, scaling/tray and real hardware cases pending.

## 5. Unresolved decisions / external gates

- Manual UX review remains separate from source review; the user may test the provided Portable.
- Real GPU testing is not currently authorized and must be requested if needed before making performance/quality claims.
- No new CUDA build is justified by the current app-only fixes.
- No GitHub/Hugging Face publish or public release is authorized by this task; local completion only.
- Code-review findings affecting the lightweight/old/Next products are retained in the historical report, not silently fixed in this Manager-only scope.
