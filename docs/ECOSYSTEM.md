# NInferEZ ecosystem

[Manager](https://github.com/BenGamliel/NInferEZ-Manager) is the recommended starting point for Windows users: model/API management, engine downloads, profiles, logs and guided/manual conversion.

[Engine](https://github.com/BenGamliel/NInferEZ-Engine) is available independently for command-line serving and integration. Choose its CUDA target for your GPU.

[Converter](https://github.com/BenGamliel/NInferEZ-Converter) provides a separate Portable desktop application and JSON backend. Support follows source importers and target geometry, not every possible model name.

[Models](MODELS.md) are hosted separately in the [NInferEZ Models collection](https://huggingface.co/collections/2beng2/ninferez-models-6acb588de431a8f2d59fc6e1). Their cards provide features, identities, licenses and measurements. No weights are bundled with application downloads.

All three applications publish version 1.0.0. Hardware qualification remains separate; untested combinations are Community Preview. The model weights are unchanged by this coordinated application release.
