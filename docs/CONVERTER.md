# Converter in NInferEZ Manager

The Converter page uses the existing independent NInferEZ Converter backend, not
its standalone graphical application. Its engine-derived converter revision is
`51cdf17108d28d7b772dfcc56a80dcf5de517291`. The inference engine is unchanged.

## Basic workflow

The default **Guided wizard** follows the standalone converter's existing workflow:
Source → Required files → Conversion options → Review & save. It uses the same
`probe`, `analyze`, `inspect` and `convert` backend operations, not another
quantization implementation. No comparison option is silently selected. Missing
configuration, tokenizer, processor, companion and weight-shard files can be
attached individually from the checklist. Analysis returns only actual prepared
plans, including unavailable options with their reason, weight sizes and precision
warnings. A final compatibility check gates the conversion of the selected plan.

**Manual setup** uses the same settings and exposes individual resource files,
named sources, shard replacements, conversion device (CPU or Auto, using the
bundled CPU-only runtime), engine-test timeout and per-weight assignments in
addition to the existing controls. Assignment formats and methods come from
backend capabilities; selector, layout, activation policy and JSON method
parameters are validated by the existing planner. A searchable weight-plan list
shows actual encodings/actions/sizes. Import/export exchanges ordinary converter
request JSON. Import never executes recipes, starts conversion, trusts Python code,
or carries forward plan authorization; custom code must be trusted again.

The Manager captures the source/resource file state and the checked plan signature.
The converter verifies both again before conversion. Changes to source files,
assignments or resources require another check; existing outputs remain protected.
The new separate acceptance Portable is `Converter Wizard Test/NInferEZ Manager`
under the 1.0.0 development root. Previous test/release packages remain unchanged.

1. Choose a source file or checkpoint folder. Indexed Safetensors checkpoints
   should be selected through their index or containing folder, not a single shard.
2. GGUF requires matching Hugging Face config/tokenizer resources. Resources beside
   the source are suggested automatically; another folder can be selected manually.
3. Choose a new `.ninfer` destination. Manager suggests its own Models directory.
   Existing model/report files are never overwritten; edit the filename if it exists.
4. Keep Auto unless a deliberate output policy is needed. Check compatibility to
   build the real conversion plan. Any settings change requires another check.
5. Review the plan and choose Convert model. Progress is based on written bytes;
   reaching 100% is not completion until validation/publication has finished.
6. After successful conversion, Add converted model to library links the output
   without copying it or loading it into GPU memory. Select appropriate inference
   settings before using the newly linked model.

The guide button is intentionally disabled until the owner supplies its URL.

## Actual supported input routes

- Qwen3.5 Dense/MoE mathematical architectures, including compatible Qwen3.6/3.8
  checkpoints/fine-tunes, through the existing converter's source adapters.
- Ordinary/indexed Safetensors and supported encoded GPTQ/ModelOpt NVFP4/FP8 inputs.
- GGUF plus matching HF resources: geometry-qualified block/ternary preservation,
  or supported architecture-adapter re-encoding. Auto is not a promise that every
  GGUF keeps its original quantization; the conversion plan reports the selected route.
- v3 NInfer entry files: lossless repacking. Recognized official v2 entries: upgrade
  through the existing identity-qualified converter tool.
- Built-in and explicitly trusted custom Python recipes supported by the converter.

Engine-supported output codecs do not make every unrelated architecture/container
convertible. Unknown mappings or missing resources fail before publication. Every
new real model still needs matching engine/hardware qualification; a structural
check does not prove numerical quality, inference speed or every optional GPU path.

## Advanced controls

Options are grouped into components, companion sources, recipes/resources,
memory/file layout, proposal head and optional GPU verification. The output
profiles, built-in recipe choices and component names come from the converter's
capabilities. Only CPU conversion is offered by this bundled CPU-only runtime.

Text is always selected. Vision/MTP/DFlash/DFlash2 are opt-in; they are not silently
added just because a name appears in a checkpoint. A checked companion path is
used only for its enabled component. Matching source weights/resources must pass
preflight. NInfer repacking preserves its existing components and disables
irrelevant quantization/component/resource modifications in the form.

Custom recipe and override fields execute Python with the interactive user's file
access. Explicit trust is required before either compatibility checking or conversion.
Never accept recipe files from untrusted parties. No shell command strings are built.

The optional `ninfer-serve.exe` field starts a real text inference test after conversion
and before publication, only following explicit confirmation. Leave it blank for
structural checks only. A GPU test needs compatible hardware and free VRAM; it does
not automatically unload a separate Manager inference session, and does not qualify
every Vision/speculative workload.

## Resource use, cancellation and isolation

No Python worker starts with Manager/API startup. Capabilities are loaded on first
opening Converter and cached. A worker starts for a requested check/conversion and
exits afterward. Only one conversion/check runs per Manager instance. Running
inference is not modified by ordinary CPU conversion, though CPU/RAM/disk contention
can affect performance. Large model conversion can require substantial resources.

Cancel sends the existing graceful cancellation protocol. The UI waits for cleanup,
not just a button acknowledgement. The source and existing output files remain intact.
If a worker cannot stop after 30 seconds, only its owned process tree is terminated;
the error explains that its temporary `.ezc-` staging folder may remain. Do not remove
folders belonging to a live conversion. A normal application Exit asks whether to
keep the conversion running in Tray, or cancel it and wait before closing.

Conversion controls are on the authenticated, loopback-only Manager control API,
not the public OpenAI-compatible endpoint. Errors and bounded worker diagnostics go
to Manager logs. Completed outputs retain their `.conversion.json` provenance report.

## Distribution and credit

`Converter/` contains only the existing backend, app-local CPU Python dependencies,
converter/adapters, host-only inspector, documentation and required licenses/notices.
The standalone converter frontend is not included. Retain the complete folder when
moving the Manager Portable. Original engine-derived Apache-2.0 notices and the
Python/PyTorch/NumPy/GGUF/.NET/native notices remain under `Converter/licenses`,
`Converter/app/vendor` and `Converter/docs`. The runtime is shared through immutable
hardlinks on this development machine to avoid duplicate disk use; ordinary transfers
must copy its complete directory tree. Model weights are external and were not copied.
