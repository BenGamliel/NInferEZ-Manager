# Model VRAM guidance (1.0.0 test Portable only)

Both installed models and download recommendations show one of three general recommendations:
green "Fits", amber "Close", or red "Not recommended". No context numbers or technical suffixes
are shown in the badge or hover explanation.

Internally, green means model plus 64K (65,536 tokens) is at most 80% of GPU capacity with at
least 2 GiB remaining. Amber means 64K is tight or only 32K (32,768 tokens) fits. Red means even
model plus 32K exceeds total capacity (or the model itself is too large). Demand exactly equal
to capacity is amber, not green. Missing size/GPU data or a different CUDA device hides the
badge rather than inventing a fourth recommendation.

Catalog working-memory and KV slopes are used only for matching cache precision/acceleration.
Local models supply their own attention geometry from a bounded read of the native v3 JSON
directory, without reading tensor payload or launching any engine process. Header metadata is
cached by resolved path, file size and write time; changing/relinking a file invalidates it.
Only supported Qwen3.5 text/MoE geometry is estimated; unsupported/malformed/missing geometry
does not receive a fabricated Close label. Unknown advice is hidden until sufficient data exist.

The geometry path prices each target KV dtype using the engine's actual K/V data and scale
plane sizes, adds MTP/full-attention draft KV or fixed sliding draft caches when enabled, and
includes a small paging allowance. Non-catalog working memory uses total artifact bytes plus
at least 2 GiB or 10% for buffers, with additional draft-buffer allowance. Declared continuation
payload counts toward total weights; a small shard entry alone cannot create a green claim.
These are estimates, not runtime calibration or memory reservation guarantees. Saved 128K, 256K or custom KV capacity
never determines badge color. Hover advice stays general and contains no numbers. All guidance remains
advisory: file size differs from runtime memory, other apps consume VRAM, and hardware/model
compatibility still matters. Green means likely room, not a promise of successful inference.

Uses the existing GPU status poll, with no extra GPU queries, inference, full-file scans, model copies
or loading-setting changes. UI recalculates on GPU capacity/identity or inventory/profile refresh,
not on transient used VRAM (avoids counting the already-loaded model twice).
Neither downloads nor model selection are blocked. Public Installer/Portable remain unchanged
until user acceptance. Version remains 1.0.0.
