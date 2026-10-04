# Security policy

Report security issues privately through GitHub's security-advisory feature. Do not put API keys,
model prompts, personal paths or unreviewed diagnostic archives in public issues. A useful report
contains the affected version/build provenance, minimal reproduction and observed impact without
private data.

## Boundaries

The public inference API defaults to loopback. Local network access is an explicit option, not a
firewall allowlist. A `0.0.0.0` listener can be reached through every permitted network interface,
including a VPN. Use authentication and review those network services independently. Opening
Manager does not authorize changing the user's firewall or VPN configuration.

The public localhost inference API may run without a key, and browser CORS is enabled by default
with an any-origin response policy. Localhost is not authentication for local applications or
browser contexts. Set a real API key and disable CORS when browser clients are not required.
Browser network restrictions are not a substitute for this explicit protection. The public
inference API is not the private authenticated application-control API.

The private control API must be loopback-only and authenticated with the random identity/token
belonging to that owned application-launch session. It controls files, processes and updates and is not an OpenAI
client endpoint. Loopback alone does not establish that the responding process is trusted.

Remote model feeds are data-only. Engine and app artifacts require official repository/source
policy, expected identifiers, bounded size, SHA-256 and safe staging/extraction. A SHA supplied by
the same untrusted caller as a URL is not proof of origin. HTTPS plus SHA checks are not claimed
to be a digital-signature system. Signed manifests and code signing must only be advertised after
actual key provisioning and verification are implemented.

Presets cannot run commands, choose arbitrary executables or supply arbitrary environment variables.
Model files are still complex input handled by native code: only load artifacts from sources you
trust. Package validation does not prove that all possible model inputs are safe.

## Data and reporting

Clean public packages must not contain settings, runtime/control credentials, personal paths, logs,
model weights, debug symbols or crash dumps. A local ready-to-test Portable is private even if it
shares application binaries with the public package.

Request/response content is not intended to be retained by Manager by default. Technical logs and
crash reports can still expose paths, aliases or input-dependent messages; inspect them before
sharing. Diagnostics should redact known sensitive fields and patterns, but automated redaction is
not a guarantee that arbitrary free-text content is anonymous.

See [the acceptance matrix](docs/ACCEPTANCE-MATRIX.md) for implemented and tested evidence. Preview
hardware status and host-only checks are not security certifications or hardware qualifications.
Never report a security gate as passed solely because a release ZIP was produced.
