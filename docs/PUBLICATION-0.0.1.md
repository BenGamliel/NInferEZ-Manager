# Public release 0.0.1

## Scope

The first public application release contains the clean Windows x64 Installer and Portable ZIP.
The private ready-to-test Portable, models, engine binaries, runtime data, caches, logs and local
settings are excluded. Engines retain their independent release and licensing lifecycle.

The executable packages were built on 2026-09-30. Publication preparation on 2026-10-04 did not
change the application or inference engine binaries. The publication commit updates source to
that application implementation and adds publication documentation, repository exclusions and
CI configuration. Native application and GPU qualification are separate from host verification.

## Observed verification

| Check | Result | What it establishes |
|---|---|---|
| Backend regression suite | 112 passed | Contracts, small-file IO and fake-engine lifecycle |
| Updater regression suite | 13 passed | Staging, transactions and rollback fixtures |
| Control/API regression suite | 45 passed | Actual managed backend with tiny fixtures, not real weights |
| Static source product checks | 13 passed | Version, required material and selected installer safeguards |
| Public Portable layout checks | 25 passed | Required files and absence of known private runtime payloads |
| Public Portable privacy audit | Passed | Bounded text/path/credential and artifact-name checks |
| ZIP inventory | Reviewed | No model weights, PDBs, runtime settings or logs |
| Source and public-payload privacy scan | No actual personal identifiers or credentials found | Pattern-based review, not an anonymity or malware certification |

The three host suites were rerun successfully on 2026-10-04 without real model loading or CUDA
inference. Previously recorded three short IQ3_XXS requests are not long-context, vision,
NVFP4 or broad hardware qualification. Exhaustive native interaction, clean install, upgrade
and additional GPU qualification are still pending. Untested hardware/model combinations remain
Community Preview. The detailed [acceptance matrix](ACCEPTANCE-MATRIX.md) preserves those gates;
its historical execution entries are not silently rewritten as new qualification.

## Package identity

| Package | Bytes | SHA-256 |
|---|---:|---|
| `NInferEZ-Manager-Setup-0.0.1.exe` | 99,003,263 | `ebafcc0ac9e662e1d8fe240950505272817b19fdcff90153e97e062eafdc14e8` |
| `NInferEZ-Manager-Portable-0.0.1.zip` | 143,911,758 | `c0510aecfe9ff0346c51161761b971377c18f26864a01cdb0a55f56987045589` |

These hashes are also distributed as `SHA256SUMS.txt` alongside the two assets. Hashes identify
bytes; they are not an Authenticode signature. The manual package-preparation workflow produces
reviewable artifacts and does not automatically overwrite a published release.
