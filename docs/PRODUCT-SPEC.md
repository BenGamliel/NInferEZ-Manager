# NInferEZ Manager — product and UX specification

Status: design baseline for review  
Date: 2026-09-28

## Product definition

NInferEZ Manager is a complete Windows control center for local NInfer
inference. It is aimed at people who want more control and visibility than the
lightweight NInferEZ app provides, without making them understand every NInfer
command-line option.

The lightweight NInferEZ product remains independent. Manager reuses a shared,
headless core for engine discovery, model lifecycle, lazy API behavior and safe
configuration, while adding model management, request inspection, telemetry,
logs, diagnostics and engine maintenance.

## Audiences

1. **Operator** — wants to choose a model, see whether the API is ready and
   diagnose a failure without opening a terminal.
2. **Power user** — wants exact context, KV, speculative decoding and lifecycle
   control, but expects safe defaults and readable consequences.
3. **Integrator** — connects another application to the OpenAI-compatible API
   and needs stable endpoint, model ID, health and request-level metrics.

The default experience targets the operator. Power-user controls are available
through contextual editors and an explicit Advanced mode, never as one long
form on the home screen.

## Product principles

- **State before settings.** Every page first answers what is running, what is
  selected and whether action is needed.
- **Progressive disclosure.** Recommended values are shown first; exact engine
  controls stay available without dominating the normal workflow.
- **One primary action.** Each state has one visually dominant next action.
- **Explain consequences.** Controls that affect VRAM, quality, cold-start time
  or compatibility show the impact before applying.
- **No silent repair.** Automatic recommendations are visible and reversible.
- **No fake precision.** Metrics identify their measurement source and unknown
  values stay unknown.
- **Inference first.** UI telemetry must not compete with the engine for GPU or
  materially change inference latency.

## Information architecture

### Overview

The operational home screen. It contains API and model state, current GPU/VRAM,
context occupancy, request activity and a short performance summary. It does
not contain the entire settings form.

Primary actions: copy API endpoint, load or unload model, inspect an active
request, open the selected model.

### Models

Library of models discovered in the product `Models` directory and persistent
external links. It supports search, filters, user-defined API names, profiles,
validation, activation and removal of application-managed downloads.

The default view is a compact list. Selecting a model opens a detail pane rather
than navigating through multiple dialog windows.

### Requests

Recent and active requests with type, state, model, prompt/output tokens, TTFT,
prefill rate, decode rate, duration and speculative mode. Prompt and response
content are not retained by default.

### Logs

Live, bounded and virtualized logs with level/component filters, search,
autoscroll pause, copy, export and a clear distinction between Manager, API and
engine events. Hidden log views do not perform high-frequency rendering.

### Engine

Installed engine build, detected GPU, architecture, compatibility, update and
rollback state. Engine packages remain replaceable independently of the UI.

### API

Endpoint, port policy, health, lazy-load state, unload policy, access scope and
copyable integration examples. LAN exposure remains off by default.

### Settings

Application, lifecycle, appearance and update defaults. Per-model inference
settings belong to the model profile editor instead of this global page.

## Responsive behavior

- Wide: 240 px navigation rail, page content and contextual detail rail.
- Medium: compact 84 px navigation rail; secondary detail moves below content.
- Narrow: navigation becomes a bottom bar; low-priority telemetry collapses;
  tables become row cards without horizontal clipping.
- Minimum supported content width is 760 px for production desktop use. The
  prototype also demonstrates a narrower emergency layout.
- Production QA covers 1366x768, 1920x1080, 4K and 100/125/150/200% scaling.

## Visual direction: Precision Console

The baseline uses neutral graphite surfaces, a restrained teal status accent,
warm off-white text, strong numeric typography and minimal borders. Color is
reserved for status and actions. Cards are grouped by task rather than used for
every label/value pair.

The design should feel like a dedicated inference appliance: calmer than a
developer dashboard, denser than a consumer launcher, and legible during long
monitoring sessions.

## Runtime architecture

```text
NInferEZ Manager UI (WinUI 3, MVVM)
              |
      authenticated local contract
              |
NInferEZ Manager Core / service host
  | model library | lifecycle | profiles | metrics | logs | updates |
              |
        versioned engine adapter
              |
          NInferEZ Engine
```

The UI never builds free-form shell commands. Profiles are schema-versioned and
validated. Engine capability manifests control which options are visible and
which combinations can be applied.

## Performance constraints

- No model or GPU allocation merely because Manager is open.
- The public API starts quickly and loads the selected model on first inference
  unless the user explicitly requests a warm load.
- Telemetry is event-driven where possible and sampled at no more than 1 Hz on
  visible pages; background/minimized sampling is slower.
- Logs and requests use bounded stores and virtualized lists.
- UI work is asynchronous and cannot block API streaming or engine stdout.
- No WebView is required by the production UI.
- Manager idle targets will be measured after the WinUI shell exists; targets
  are not claimed before measurement.

## Delivery gates

1. Approve information architecture and visual direction using the interactive
   prototype.
2. Build a WinUI shell with fake data and verify responsive behavior visually.
3. Extract/adapt the shared headless core with unit and contract tests.
4. Connect status, library and logs before adding write actions.
5. Connect lifecycle, profiles, downloads, update and diagnostics workflows.
6. Build Portable and Installer artifacts and run clean-machine QA.

The first approval gate intentionally prevents backend completeness from being
used to justify a poor or overloaded interface.
