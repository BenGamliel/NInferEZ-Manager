# Workspace cleanup proposal — 2026-09-30

Status: pending user approval. No existing application, model, runtime or research directory has been deleted.

## New Manager packages

Product version remains 0.0.1. The current development build is dev-014.
Final local packages are in `artifacts/0.0.1/final/packages`: Installer, Portable,
and Test Portable. They include the LAN option, installed-model API inventory,
request model selection, editable API names in Models, and native Windows window icons.
Test Portable includes the current sm120a runtime. Public packages do not include
personal settings or model paths. Installed/linked model files are not copied.

Final package files use NTFS hard links to dev-014 to avoid storing the same binaries twice.
Future builds must use new build directories, rather than modifying linked binaries in place.
65 package hashes and release privacy audits passed. Backend tests do not load a model.
Actual GPU model switching and visible title-bar icon confirmation remain user checks.

## Keep

- `NInferEZ Manager`: source, Git history, assets, feeds, installer, scripts, tests,
  documentation, dev-014 and final packages. Remove earlier builds only after migrating
  any useful user settings, linked models and profiles.
- `NInferEZ Suite/NInferEZ`: the original lightweight app, its source and Git history,
  profiles, documentation, and latest Installer/Portable artifacts.
- `NInferEZ Suite/NInferEZ Engine`: source, Git history, licenses, manifests, third-party
  dependencies, build configuration, and the sm86/sm89/sm120a engine distributions.
  Keep build environments required to rebuild those engines; no blanket deletion of engine caches.
- `NInfer Manager WinUI`: the latest old NInfer Manager, product version 1.1.0,
  its source, assets, build scripts, tests, runtime and latest distributions.
  Its README is stale; the project version and packaged executables identify 1.1.0.
- Every unique model artifact, original source model, conversion recipe, tuned profile,
  benchmark result, calibration data, and model documentation required for later development.
- Original branding assets and project plans.

## Delete after preservation and approval

- Manager dev-001 through dev-013 (approximately 33.06 GiB), older `dist/0.1.0`,
  obsolete build output and recoverable compiler output. Retain dev-014 and final.
- `NInfer Manager 1.0.0 New Engine` (8.52 GiB): superseded by the old Manager 1.1.0.
  Extract any unique profiles or documentation first.
- `NInfer Manager App` (3.75 GiB), `NInfer Manager App 2` (0.13 GiB),
  `NInfer Manager Next`: superseded prototypes. Preserve any unique source patch or
  engine provenance needed by the retained products before deletion.
- `NInferEZ Portable Test` (1.60 GiB) and `NInferEZ Portable Test 0.1.0-preview.1`
  (0.28 GiB): redundant lightweight app test copies; preserve useful user configuration.
- Superseded lightweight NInferEZ builds and old Manager package copies, leaving only
  each product's latest requested distributions.
- Empty `den_llama.cpp` and `Model Transfer` directories.
- `VLLM Manager App` (0.57 GiB): unrelated application; proposed removal under the user's
  request to retain only the specified products. Requires approval of this list.
- Identical model copies embedded in obsolete distributions: delete only after SHA-256
  confirms equality and one retained canonical file has been verified.

## Reorganize before deleting dependent directories

- Create a central `Models` area for unique models, sources, conversion scripts,
  profiles and evidence. Move files on the same drive, rather than copying large models.
- Swift NVFP4 currently lives under `NInfer Manager WinUI/build-cache/models`; it must
  survive any cleanup of build-cache. There is another Swift copy in the old full package.
- The IQ3 original and DFlash2 artifacts currently live in the OrcaRouter model project;
  retain both until their roles are documented. They are different artifacts, not duplicates.
- Preserve the regular Qwen NVFP4 test model and original HF source weights as unique
  model inputs. Do not delete these merely because their parent is named build-cache.
- Preserve `Vast Results`, the useful source/experiments from `NInfer Performance Lab`,
  benchmark reports and root evaluation scripts under a research/evidence area.
  Remove redundant runtimes and recreatable outputs only after their dependencies are checked.
- Retain settings and profiles from previous test packages locally in the retained products'
  Data area. Never place personal paths or keys in public packages or Git.
- Update model links, preset paths and developer scripts after moves; verify file existence
  and profiles before removing old directory structures.

## Execution gate

This is a proposal, not a deletion manifest already executed. After approval, resolve every
target to an absolute path within this workspace, check running processes and nested Git
repositories, preserve unique information, then delete only the approved targets.
Final freed disk space must be measured after cleanup; directory size estimates include duplicates
and do not correctly count hard links as separate physical allocations.
