# NInferEZ Manager 1.1.0 updates

## One product update

Engine changes and interface changes are delivered through a Manager release.
Manager 1.1.0 pins one corrected Engine 1.1.0 archive for each supported target:
sm86, sm89 and sm120a. Each pin contains the exact version, contract, filename,
URL, byte size and SHA-256. An interface-only future release can retain its
previous tested engine pins. There is no separate engine-update choice.

After Manager starts, it automatically downloads and verifies the pinned engine
for the detected GPU architecture. The previous engine remains selected while
the new archive is downloaded, hashed, extracted and validated. Activation uses
the engine lifecycle lock. New model loads are blocked until setup completes; a running
model is never switched to another executable underneath a request. A download,
hash or extraction failure leaves the previous pointer intact and offers retry
through Check for updates. Existing package reuse requires the exact verified
archive receipt and matching hashes for its installed files.

## Finding an update

In Manager 1.0.0, automatic checks are attempted when the application starts, if
Auto check updates is enabled and the saved check interval has elapsed. The
default interval is 24 hours. A successful check can show an update notice; it
does not download or install the update without approval. The old application
has no continuous update-push subscription, so an already-open window is not
guaranteed to discover a newly published release immediately.

In Manager 1.1.0, use **Check for updates** at the bottom of Settings or from
the tray to check immediately. A manual check bypasses the automatic-check delay.
When an update is known, its version and **Download and install** are available
in Settings and in the update banner at the top of the window. The banner stays
until closed with its X; closing it leaves that update available in Settings.
Other notices still dismiss automatically. Download and install checks the
latest release again and goes directly to it, even if several releases were skipped.
Check for updates also retries incomplete setup of the engine that ships with
the current Manager release.

## Packages and data

Portable clients download the Portable ZIP; installed clients download Setup.
Both Manager package types fit the existing 1.0.0 updater's 1 GiB package bound.
The engine archive is downloaded separately and automatically after the app
update, using the pinned engine size/hash checks.

Application updates retain saved profiles, model files, logs and existing engine
versions. Portable updates preserve the portable.mode marker. Setup excludes and
removes portable.mode from its target, including when installing over a Portable
tree, so future installed updates select Setup rather than the Portable ZIP.
Data, Models and Engine are preserved during that transition. External model
links continue to refer to their original files; model weights are not bundled.

The stable Converter 1.0.0 file contents are unchanged. Its verified container ZIP
is stored inside Backend to stay within the original updater's component and
5,000-entry limits. It is extracted atomically into Data/runtimes on first use.
ZIP timestamps/container serialization can differ without changing any Converter
file bytes. Existing legacy Converter locations remain supported.

## Initial 1.0.0 upgrade and recovery

The original portable helper replaces the application, Backend, Assets and Docs
and retains application backups until startup is acknowledged. The new UI
acknowledges startup only after its matching-engine setup completes. If its
90-second confirmation wait expires while the app is still running, the old
helper keeps the recovery files; it does not roll back over the live app. Large
engine downloads can outlast that wait. A later acknowledgement is safe; retained
recovery files can be confirmed when the next update/recovery operation examines
them.

The 1.0.0 bootstrap does not provide a single atomic transaction spanning app
files and an external engine pointer. Portable app-file recovery and engine
selection are separate. Installed updates use the per-user installer and preserve
engine/model data, but do not promise an atomic app-and-engine rollback either.
If setup fails, retain recovery files and use the verified update/recovery path;
do not delete backups or replace DLLs while Manager or its engine is running.

## Catalog and version requirements

Manager 1.1.0 reads feeds/model-catalog-v2.json. OmniEdu requires both Manager 1.1.0
and actual Engine 1.1.0 or later. Its recommended profile retains 128K Int8 KV,
BF16 overlay Vision, FP32 recurrent state, DFlash2 and author sampling settings;
it does not rewrite model weights. The legacy feed omits models whose version
requirements 1.0.0 cannot enforce. Old clients receive the normal Manager update
notice when their update check runs.

Download and launch enforce numeric stable x.y.z minimum versions. A prerelease
version string does not satisfy a stable minimum; hardware-preview qualification
is separate from the numeric engine version. Requirements survive caching and
preset import. A renamed local artifact matching a version-gated catalog size is
hashed and associated by SHA; the verified identity is reused while its size and
write timestamp remain unchanged. Changes invalidate that cached verification
without weakening the recorded minimum versions.

## Hardware coverage

All three pinned packages contain the corrected 1.1.0 source. RTX 5090 has real
hardware qualification for sm120a. sm86 and sm89 are build-verified previews:
their matching RTX 3000/RTX 4000 hardware has not been tested here. Other sm120a
cards are also hardware-unverified. A successful build, forward-PTX diagnostic,
matching architecture or valid artifact does not establish qualification for a
particular GPU/model/context combination. Available VRAM and actual model
components still determine whether a profile fits.
