# NInferEZ Manager 1.1.0 — user guide

## Start with a model, not a terminal

Install Manager or extract the complete Portable package to a writable folder. Keep the EXE,
Backend and Assets together. A Portable is launched from its extracted folder, not from the ZIP.

On first launch, Manager automatically sets up the exact engine package pinned
for the detected GPU architecture. Let its download and integrity verification
finish before loading a model. **Engine** shows runtime details; engine changes
arrive through Manager updates. An already-installed matching verified pin can
be reused offline. A package for another architecture is not an upgrade.

On **Models**, open the displayed Models folder and put a supported `.ninfer` file there, or
**Link a model** from another location. A link does not duplicate the file. Refresh the library if
necessary. Do not rename or move a linked file while it is loading or serving requests.

Portable uses its adjacent Models folder. Installed Manager's writable default is
`%LOCALAPPDATA%\NInferEZ Manager\Models`; it also discovers supported files placed in the
Models folder next to the installed application. Use **Open folder** to see the actual default.

Select a model, inspect its profile and copy the API endpoint and model name. You can set a shorter
or otherwise preferred API name yourself. The application does not invent aliases. Two available
models cannot use the same API name.

## Connect an API client

Normally the Base URL is:

```text
http://127.0.0.1:8173/v1
```

Use the exact model name shown in Manager. A `provider/model` prefix is a convention of some
clients, not part of the Manager model ID unless you entered that exact alias. For clients with a
separate provider choice, select their OpenAI-compatible/custom provider and supply the Base URL.

If an API key is configured in Manager, your client must use the same key. If you have not
configured authentication, a dummy client key does not make the endpoint protected. The Models
endpoint lists the locally available model IDs without loading model weights.

Localhost limits network reach, but it does not authenticate other programs on the PC. Browser
client access (CORS) is initially enabled and allows any origin in the API's response headers.
Set a real API key, and disable CORS when browser clients are not needed; do not rely on browser
network restrictions as API authentication. This public inference API is separate from the
private, session-authenticated application control API. Localhost-without-key is not a universally
secured configuration.

The API starts with the application. You do not have to press Load first: the first inference
request loads its named model. **Load** is useful when you want to warm it before the next request.
Only the required model is loaded; advertising two models does not imply that both occupy VRAM.
Concurrent requests may be rejected while the engine is busy; wait and retry instead of launching
a second application instance.

## Loading and memory

First loading can involve route calibration, loading weights and allocating context. The displayed
phase and elapsed timer show progress; no precise percentage is shown when the engine does not
provide one. A long first calibration is different from a broken HTTP connection. **Cancel loading**
requests cancellation and remains in a cancelling state until the owned process has stopped.

The default policy unloads a model after three idle minutes. Choose a different interval,
immediate unload or keep-loaded in Settings if that better matches your workflow. Unloading saves
model VRAM but means the next generation includes a cold load. It does not close the public API.

Configured context is a capacity, not proof that a request contains that many tokens. A 512K
window is not an instantaneous 512K prefill. Auto recommendations are memory estimates based on
available hardware and artifact information; they do not guarantee every request or every model
fits. The weights alone may already exceed the available budget.

Custom profile settings are deliberate. Changing KV precision, speculative mode or context can
affect memory, speed and sometimes output quality. Use a recommended profile appropriate to the
actual artifact; a similarly named file may contain different drafter or vision components.
For an unfamiliar artifact without calibrated memory costs, the initial explicit 32K context
(clamped to a known model limit) is a starter value, not a guarantee of fit or an Auto estimate.
Changing a saved profile does not rewrite an engine session already running. Reload when needed
and compare the displayed loaded context with your saved request.

Use the model's profile import/export actions for a `.ninferez.json` settings file. Keep an adjacent
file with the matching model when sharing a preset. A declared SHA in settings is identity metadata,
not proof that this computer has read and verified every byte of the weights. Verification is a
separate potentially expensive disk operation. An imported profile can retain custom local template,
context-cost or log paths: review those paths before sharing, and ensure required resource files
exist on the receiving computer. Do not share credentials or private paths in a preset.

## Models and downloads

The local library and the curated recommendations are separate. A recommendation is not already
installed. Download progress includes verification; reaching the expected transfer size does not
mean SHA verification has completed. Pause/cancel and resume apply only where the downloader can
continue the identified partial safely.

Do not delete `.part` files while a download is active. Network loss, a hash mismatch or disk-full
failure should be treated as a failed transfer, not a model ready to use. Keep enough free disk
space for partial/staged downloads and updates.

A linked file that disappears is Missing. Restore/relocate the file or remove its link. Removing a
link does not delete the external file. Removing a model managed inside the application Models
folder is a different destructive action and should be confirmed. Models with missing components
cannot gain vision or DFlash2 merely by enabling a checkbox.

## Settings and engine maintenance

Ordinary preferences save automatically. Invalid input is not silently applied. Model profiles use
their local save/restore actions; changes to API port, key or network scope use the explicit Apply
action so that a partially typed port does not restart the listener.

The **Engine** page shows selected/runtime details. Manager updates own the exact
engine version and verified archive for each supported architecture. There is no
independent engine update to choose. Never replace DLLs inside a running engine.

RTX 5090 has real sm120a hardware qualification. Corrected sm86/sm89 packages are
build-verified previews, with matching RTX 3000/RTX 4000 hardware untested here.
Other sm120a cards also remain hardware-unverified. Native NVFP4 is not implied
on older architectures. Driver, artifact components and available VRAM still
matter; a matching target does not mean every card/model/profile was tested.

## LAN and VPN clients

Local network exposure is disabled initially. Enabling it binds beyond loopback and can include
VPN interfaces. Configure an API key, review who can reach the port and apply the change. Use the
PC's reachable network/VPN address in a remote client, not `127.0.0.1` (which means the client's own
device). Manager does not automatically open the Windows firewall or change VPN permissions.

## Tray, logs and notifications

Closing to tray can leave the API active. Use the tray controls or exit the app if you want it
stopped. Opening the EXE again should activate the existing window, not create another manager.

Success/error notices normally close after ten seconds. Hover retains a notice; if its deadline
has passed it closes when the pointer leaves. Persistent progress belongs on the corresponding
card. Errors include a human explanation; **Logs** provides the technical details.

Requests metrics come from the engine's recent history and may be unavailable. They are not proof
of a fixed benchmark context. Before sending diagnostics, inspect the contents for paths, aliases,
credentials or other sensitive input-dependent messages. Automated redaction cannot guarantee that
arbitrary text is anonymous.

## Updates and data

Use **Check for updates** in Maintenance or the tray for an immediate check.
Automatic checks run at startup when enabled and the saved interval has elapsed
(24 hours by default). An already-open1.0.0 window has no continuous update-push
subscription. Checking never installs automatically: approve the Manager update,
then its matching pinned engine is set up automatically after the app restarts.
An active inference must finish or be deliberately stopped before replacement.

Updates preserve models, saved profiles, logs and existing engine versions. Setup
also removes a portable.mode marker already in its target so future installed
updates choose Setup. Portable updates retain that marker. The original 1.0.0
helper's app-file recovery and engine selection are not one atomic transaction;
keep recovery files if startup/setup is interrupted. Installed updates request
relaunch after installation. See [the update contract](UPDATES-1.1.0.md) for exact
verification, retry and recovery behavior.

OmniEdu requires Manager 1.1.0 and actual Engine 1.1.0 or later. Manager 1.1.0 uses
the versioned catalog and enforces both requirements. This requirement does not
promise that every GPU can fit the recommended 128K profile.

Portable data stays beside the application. Installed data uses the per-user Manager data root.
Uninstall retains managed data by default; explicit removal never follows external model links.
Back up your profiles and settings before manual directory changes. Keep local test data separate from clean distribution packages.

## Quick troubleshooting

| Symptom | What to check |
|---|---|
| API cannot connect | App/API listening state, actual displayed port, client Base URL, LAN scope and firewall/VPN access |
| Unknown model | Copy exact listed API name; check file still exists and alias has not changed |
| Unauthorized | Client key matches Manager; a provider prefix or dummy key does not substitute for it |
| Model never becomes ready | Loading phase, cancellation state and log; calibration is not the same as inference readiness |
| Insufficient memory | Weight size, loaded context, selected device, competing GPU use and workspace reserve |
| Unsupported vision/speculation | Actual artifact components and selected engine capabilities, not the model-family name |
| Download verification failed | Correct artifact/revision, available disk space and safe Retry/Resume; do not use partial as final |
| Save rejected | Field range, profile compatibility or stale settings revision; refresh rather than overwriting other changes |
| Update failed | Keep recovery directory and prior package; inspect logs and recovery guidance before manual deletion |

Release-specific verification and hardware coverage are recorded in the accompanying release evidence.
