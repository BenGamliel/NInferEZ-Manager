# NInferEZ Manager 1.1.0

- A single Manager update now includes automatic setup of its exact pinned
  Engine 1.1.0 package for sm86, sm89 or sm120a. Archive hashes, extracted identity
  and installed-file receipts are verified before reuse or activation.
- Start **Download and install** from the update banner or the Updates section
  at the bottom of Settings. The banner stays until dismissed with X. The
  update remains available in Settings, and installing always checks for the
  latest release first.
- Model requirements enforce minimum Manager and actual engine versions. The
  versioned catalog includes OmniEdu with a 1.1.0 minimum for both applications.
- OmniEdu's 128K Int8 KV profile preserves its Q6_K text weights, BF16 overlay
  Vision, FP32 state, DFlash2 and original author sampling settings.
- Portable and installed update paths preserve user data, models and existing
  engine files. Setup removes a Portable marker from its installation target so
  later updates use the correct package type.
- Converter 1.0.0 file bytes are unchanged; its verified container is repackaged
  for compatibility with the original updater.

RTX 5090 has real sm120a hardware qualification. Corrected sm86/sm89 packages are
build-verified previews, with matching hardware untested. Other sm120a cards also
remain hardware-unverified. See the Engine release evidence for detailed coverage.

Automatic checks run at application startup when enabled and the saved interval
is due (24 hours by default). Check for updates performs an immediate manual
check. Installation still requires approval; the matching engine then completes
automatically as part of that update.

The original 1.0.0 helper's recovery is not an atomic app-and-engine rollback. See
UPDATES-1.1.0.md for package selection, preserved data and recovery behavior.

## Bug fixes

- Fixed model loading and inspection errors for Unicode file paths on Windows.
