# NInferEZ models

Download these artifacts from Models in Manager or link an existing local file. Choose a compatible engine and leave managed settings enabled for an appropriate starting profile.

| Model | File size | Components | Model card |
|---|---|---|---|
| OrcaRouter IQ3_XXS, MTP only | 10.06 GiB | Text, MTP | [Details and measurements](https://huggingface.co/2beng2/Qwen3.8-27B-OrcaRouter-GSQ-RCO-IQ3_XXS-NInfer) |
| OrcaRouter IQ3_XXS, MTP + DFlash2 | 12.13 GiB | Text, MTP, DFlash2 | [Details and measurements](https://huggingface.co/2beng2/Qwen3.8-27B-OrcaRouter-GSQ-RCO-IQ3_XXS-NInfer) |
| Swift 1.5 NVFP4 | 21.22 GiB | Text, vision, MTP, DFlash2 | [Details and license](https://huggingface.co/2beng2/Swift-1.5-Qwen3.8-27B-Uncensored-NVFP4-NInfer) |
| Heretic IQ2_XS | 7.84 GiB | Text only | [Details and source](https://huggingface.co/2beng2/Qwen3.8-27B-Heretic-GSQ-RCO-IQ2_XS-NInfer) |

File size is not total VRAM use: KV cache, draft components, runtime state and workspace need additional memory. Fits/Close/Not recommended is an estimate, not a guarantee. Native NVFP4 requires a compatible Blackwell target; IQ3/IQ2 use different execution paths.

The recommendation catalog links only to NInferEZ-published conversions. Other compatible models can still be linked or placed in the local Models folder. Runtime support follows architecture, encoded tensors, embedded components and GPU target, not the publisher or filename. Artifact identity includes repository revision, size and SHA256; a same-named file is not necessarily the same model.
