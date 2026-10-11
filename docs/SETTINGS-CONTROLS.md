# Settings controls and managed profiles

Product version is 1.0.0. This source tree is isolated for release packaging; the existing application,
published release, model artifacts and inference engines are not replaced.

## Everyday use

Choose the profile's context ceiling, KV precision, acceleration and optional image input. Turn
on **Manage advanced settings for me** to coordinate the dependent settings. **Save profile** is
still explicit: unsaved choices do not restart a loaded session. New recommended profiles use
managed mode. Existing and exact imported presets stay manual until the user opts in; their
numerical parameters are not silently rewritten by migration.

The toggle captures every declared advanced field before enabling management. Switching it off
restores those fields, while preserving the latest basic choices. The backup persists in the
profile and survives a restart. Repeated managed edits do not overwrite it. The backup contains
only allowlisted scalar fields, never an executable, environment map or arbitrary command.

Managed mode follows the model's recommended advanced values. Explicit context uses matching KV
capacity; automatic fitting delegates KV allocation to the engine after weights are loaded,
instead of claiming that a catalog VRAM estimate is a hardware measurement. KV precision and
model weights are not automatically downgraded. Memory and hardware limitations still exist:
even a minimal working cache may not fit, and a supported architecture does not qualify every
model, precision or card.

## Dependent controls

- Disabled acceleration: LM-head, N-gram, MTP window and draft-token controls are inactive.
- DFlash: MTP-only window is inactive. MTP: a custom window must cover draft tokens plus one.
- KV MatchContext: custom capacity and automatic headroom controls are inactive.
- Combined RAM cache: separate host KV/state controls are inactive.
- Non-vision/vision off: image budgets are inactive.
- Thinking off: thinking budget is inactive. Preserving previous thinking is a separate policy.
- Greedy sampling: probability sampling knobs are inactive.
- Managed mode: advanced fields are readable but not directly editable.
- Alternative prefix cache: default-cache state slots and continuation limits are inactive;
  their flags are omitted entirely (even zero is an incompatible explicit flag).

Text-only launches omit media budgets entirely; `media-live-mib=0` is rejected by the engine even
if image input is disabled. Vision launches require positive live media budgets. Validation
follows the current engine CLI: positive queue/history budgets, at most 64 media workers,
prefill batches divisible by 128, custom KV capacity at least the context ceiling, output limit
no greater than the context ceiling and supported sampling bounds. Managed batches are aligned
without changing model weights or the selected cache precision. Manual errors name the field
and explain the required value; logs retain the complete engine diagnostic.

Inspected unsupported acceleration modes are not offered in managed mode. If an old saved mode
has become unsupported, the editor selects Disabled and explicitly asks the user to save;
the saved profile is not silently overwritten. Manual/imported exact modes remain visible with
their incompatibility warning. Unknown metadata is not treated as confirmed support.

Model cards, Overview and Engine show a short general quantization label, not a list of every
internal tensor format. Mixed unknown artifacts are labelled Mixed quantization rather than
inventing a dominant format; full format metadata remains available to diagnostics.

Inactive controls are grey, not hidden. Their effective display follows the active mode; retained
manual values are kept in the editing profile/backup. Help remains readable even when the input
is disabled. Advanced settings are grouped in expanders. Each field has a labelled question-mark
tooltip in a small outlined circle. Help opens on hover, not on click; clicking the icon inside
a checkbox does not toggle that setting. The input and help have descriptive automation names.

The backend independently strips stale, incompatible drafting flags on cloned effective
profiles. It does not rely on the UI and does not destroy the stored manual setup. Engine CLI
sampling bounds are validated before launching. Startup failures retain the actual argument or
fatal error instead of replacing it with the tail of the engine's usage text.

## Verification and remaining acceptance

Host-only tests cover the settings policy matrix, manual backup/restore/restart, actual argument
construction for disabled acceleration and 32K KV, malformed snapshot keys, engine sampling
bounds and error retention. Existing backend/control/update regressions are retained. WinUI
compilation is separate from interaction acceptance.

No real GPU inference, full model hash or hardware benchmark is required for this UI build.
The user must approve the visual arrangement, tooltip behavior and manual/managed round trip
in the separate Portable. The 4080 SUPER loading workflow still requires testing on that card;
host tests do not establish a fitting context or generation speed.
