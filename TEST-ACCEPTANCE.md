# Manager Converter acceptance — 1.0.0

## Scope and location

This source preview extends the previous approved Manager preview with a native
Converter page. Product version remains 1.0.0. Its separate ready-to-test Portable
is `../../Converter Test/NInferEZ Manager` relative to this preview directory.
Previous Installer, Portable, Test Portable, inference engines and model files are
not replaced. Saved aliases/profiles and external model links were copied into the
new test's separate Data directory; model weights were not copied.

## Wizard / Manual acceptance update

The current separate test is `../../Converter Wizard Test/NInferEZ Manager`.
`Converter Test` and the final release packages remain intact. Both modes use the
existing standalone converter backend, whose `app/backend` files were checked
against its current source. No standalone graphical frontend is bundled.

Implemented additions: four-step guided workflow, read-only source discovery,
missing-file attachment/checklist, real plan comparison with explicit selection,
precision warnings, manual configuration/resources/named sources/shard overrides,
per-weight assignments, searchable weight preview, CPU/Auto device, verification
timeout, request JSON import/export and safe custom-code reauthorization.

Settings are shared between modes. Source or conversion-setting changes invalidate
preflight. The Wizard compares its selected plan and scanned file state against
the final check, and requires rescanning if they differ. Conversion reuses the
existing backend's plan-signature/file-state guard. Invalid/truncated headers are
presented as an actionable file error; raw worker diagnostics stay in logs.

Focused verification only: 32 converter-service checks passed (run with
`--converter-only`), and the packaged API/real CPU bridge checks are recorded in
`artifacts/converter-wizard-smoke-results.json`. These use a tiny two-layer fixture,
not the user's models. They cover discovery, alternatives, manual BF16 assignment
inside a Q4 plan, actual artifact publication, repack planning and changed-source
rejection. No GPU conversion, inference, large-model test or native click-path
verification was performed. User visual acceptance remains pending.

Suggested UI acceptance: choose Converter → Guided wizard; check a source, attach
missing files, scan, explicitly select an option and review it. Switch to Manual
setup to confirm settings are shared, and try Import/Export, resource pickers and
weight assignments. Avoid converting large models or enabling optional engine
verification while the GPU is busy. The guide link remains intentionally disabled
until its URL is supplied.

## Implemented flow

- Sidebar Converter tab, existing theme/cards/icon and expanded/compact navigation.
- Source file/checkpoint folder, suggested output inside Models, resources-folder
  detection plus manual folder picker. Output folder selection never uses an
  overwrite-capable save picker or deletes an existing user file.
- Auto plus real backend-reported profiles/recipes/components; grouped advanced
  companions, resources, chunk/shard layout, proposal head and optional engine test.
- Hover help, explicit custom-Python trust and clear GPU-test confirmation.
- NInfer repacking/upgrading disables irrelevant profile/component controls.
- Real preflight required for the exact selected settings; changes invalidate it.
- On-demand independent converter backend, single job, bounded diagnostics,
  NDJSON progress, graceful cancellation and actionable errors.
- Output/report publication is owned by the existing converter. Completion offers
  linking into the Manager library without model copying or GPU loading.
- Tray continues CPU work; normal Exit asks to keep running or cancel/wait.
- Conversion guide UI is designed but disabled; owner URL is still pending.

See `docs/CONVERTER.md` for support limits, licensing, use and resource behavior.
No new inference-engine implementation was necessary. Existing UI frontend from
the standalone converter is not reused or bundled.

## Evidence

Final host test totals are recorded in `artifacts/verification/host-results.json`.
Tests cover Manager regressions, private control API, typed conversion validation,
lazy startup, capabilities caching, exact-preflight gating, single-job conflicts,
byte progress, safe cancellation, readable errors and no-overwrite checks.

`artifacts/converter-smoke-results.json` records 11 successful checks against the
packaged Manager and real packaged independent converter. A tiny complete 2-layer
CPU checkpoint was inspected, converted, structurally validated by the native
host-only inspector, published with its conversion report and linked into Models.
The public inference engine remained Unloaded throughout. No real model weights,
GPU inference, GPU conversion or heavy model test was performed.

The end-to-end check caught and fixed an integration bug: request JSON must be one
compact line rather than Manager's indented settings JSON. A dedicated regression
check now prevents recurrence. WinUI publishing verifies XAML/event wiring, but is
not a visual or click-path acceptance test. User visual acceptance is pending.

## User acceptance

1. Close another Manager using port 8173 before opening this separate test copy.
2. Check the Converter tab in both themes and collapsed/expanded sidebar.
3. Select a source and resources; verify automatic suggestions, profile choices,
   hover explanations, and advanced controls without launching heavy work.
4. On a small supported source, run Check then Convert, review progress and output.
5. Change a setting: Convert must disable until another successful Check.
6. Test unsupported/missing resources and an existing destination: show readable
   errors without changing source/output. Cancel must wait for cleanup.
7. Add a completed model to the library; it must not load GPU memory automatically.
8. Leave the optional engine test blank while GPU is in use. GPU validation and
   large conversions require separate coordination with the user.

## Distribution boundary

Only this separate local test copy is delivered for acceptance; no public release,
Git push, Installer or other Portable has been changed. Converter runtime/licensing
remain complete under Converter. Development-host hardlinks share immutable runtime
and engine files to reduce storage, with interactive-user access preserved. Copy
the complete Portable tree for transport; do not distribute test Data/settings.
