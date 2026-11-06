---
license: other
library_name: llama.cpp
pipeline_tag: image-text-to-text
base_model: lhpku20010120/Omni-Edu-27B
tags:
- gguf
- omniedu
- education
- qwen3_5
---

# Omni-Edu-27B GGUF

GGUF versions of [Omni-Edu-27B](https://huggingface.co/lhpku20010120/Omni-Edu-27B), converted and quantized with [llama.cpp](https://github.com/ggml-org/llama.cpp). The upstream model's license and usage terms apply.

## Download

Choose one main model:

- [`OmniEdu-27B-BF16.gguf`](./OmniEdu-27B-BF16.gguf) — 50.11 GiB; unquantized.
- [`OmniEdu-27B-Q8_0.gguf`](./OmniEdu-27B-Q8_0.gguf) — 26.63 GiB; 8-bit quantization.
- [`OmniEdu-27B-Q6_K.gguf`](./OmniEdu-27B-Q6_K.gguf) — 20.57 GiB; 6-bit quantization.
- [`OmniEdu-27B-Q4_K_M.gguf`](./OmniEdu-27B-Q4_K_M.gguf) — 15.41 GiB; smaller 4-bit mixed quantization.

For image input, also download [`mmproj-OmniEdu-27B-F16.gguf`](./mmproj-OmniEdu-27B-F16.gguf). It is shared by all four versions of this model.

## Run

```bash
llama-server -m OmniEdu-27B-Q4_K_M.gguf --mmproj mmproj-OmniEdu-27B-F16.gguf --reasoning off -c 8192
```

Replace the main model filename to use another quantization. Connect Cherry Studio to `http://127.0.0.1:8080/v1`.
