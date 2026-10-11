namespace NInferManager.Contracts;

public static class ModelRecommendations
{
    public static IReadOnlyList<IReadOnlyList<ModelInfo>> Group(IEnumerable<ModelInfo> models) => models
        .Where(x => x.Featured)
        .GroupBy(x => x.RecommendationKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => (IReadOnlyList<ModelInfo>)group.OrderBy(x => x.VariantOrder).ThenBy(x => x.SizeBytes).ToArray())
        .ToArray();

    public static ModelInfo Select(IReadOnlyList<ModelInfo> variants, string? previousFile = null)
    {
        if (variants.Count == 0) throw new ArgumentException("A recommendation needs at least one model variant.", nameof(variants));
        return variants.FirstOrDefault(x => x.FileName.Equals(previousFile, StringComparison.OrdinalIgnoreCase))
            ?? variants.FirstOrDefault(x => x.Installed && x.Active)
            ?? variants.FirstOrDefault(x => x.Installed)
            ?? variants[0];
    }
}
