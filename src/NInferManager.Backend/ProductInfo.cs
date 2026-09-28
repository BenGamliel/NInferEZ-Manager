namespace NInferManager.Backend;

public static class ProductInfo
{
    public const string Version = "0.0.1";
    public const string EngineRepository = "BenGamliel/NInferEZ-Engine";
    public const string EngineChannelUrl = "https://raw.githubusercontent.com/BenGamliel/NInferEZ-Engine/main/channel-manifest.json";
    public const string AppRepository = "BenGamliel/NInferEZ-Manager";
    public const string AppReleasesApi = "https://api.github.com/repos/BenGamliel/NInferEZ-Manager/releases/latest";
    public const string ModelCatalogUrl = "https://raw.githubusercontent.com/BenGamliel/NInferEZ-Manager/main/feeds/model-catalog.json";
    public const int MinimumEngineContract = 1;
    public const int MaximumEngineContract = 1;
}
