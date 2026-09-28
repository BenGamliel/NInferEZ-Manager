# Security policy

Please report security issues privately through GitHub's security-advisory feature. Do not include
API keys, model prompts, personal paths or diagnostic archives in public issues.

NInferEZ Manager binds its public OpenAI-compatible API to loopback by default. Engine packages and
application updates are accepted only from the official repositories and are verified before use.
Model catalog entries are data-only and cannot select executables or add arbitrary command-line
arguments.

Preview builds and Community Preview GPU packages have not completed the same hardware qualification
as stable releases. Their status is shown in the application and release notes.
