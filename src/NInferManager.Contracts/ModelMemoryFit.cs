namespace NInferManager.Contracts;

public enum MemoryFitLevel { Unknown, Comfortable, Close, ExceedsCapacity }

public sealed record ModelMemoryFit(MemoryFitLevel Level, string Label, string Explanation)
{
    public const int ReferenceContextTokens = 65536;
    public const int MinimumContextTokens = 32768;
    public const int FitMinimumContextTokens = 16384;
    public const int FitContextStep = 1024;

    public static int? EstimateContext(ModelInfo model, GpuStatus? gpu, ModelProfile profile) =>
        EstimateContext(model, gpu, profile, out _);

    public static int? EstimateContext(ModelInfo model, GpuStatus? gpu, ModelProfile profile, out double bytesPerToken)
    {
        var hasEstimate = TryEstimateWorkingSet(model, profile, out var workingMiB, out var kvSlope, fit: true);
        bytesPerToken = kvSlope * 1024;
        if (gpu is null || gpu.TotalMiB <= 0 || gpu.DeviceIndex != profile.Device || model.SizeBytes <= 0)
            return null;
        var ceiling = Math.Min(profile.MaxContext, model.MaximumContext);
        if (ceiling < FitMinimumContextTokens) return 0;
        if (!hasEstimate) return null;

        var budget = (double)gpu.AvailableMiB - profile.KvHeadroomMiB;
        if (budget < workingMiB + kvSlope * FitMinimumContextTokens / 1024d) return 0;
        var estimate = kvSlope > 0 ? (budget - workingMiB) / kvSlope * 1024d : ceiling;
        return RoundFitContext(estimate, ceiling);
    }

    public static int RoundFitContext(double tokens, int ceiling) =>
        (int)Math.Clamp(Math.Floor(tokens / FitContextStep) * FitContextStep, FitMinimumContextTokens, ceiling);

    public static ModelMemoryFit Assess(ModelInfo model, GpuStatus? gpu)
    {
        if (gpu is null || gpu.TotalMiB <= 0 || model.SizeBytes <= 0
            || model.RecommendedProfile.Device != gpu.DeviceIndex)
            return new(MemoryFitLevel.Unknown, "", "A recommendation needs model size and memory information for the selected GPU.");

        var fileMiB = ArtifactMiB(model);
        var capacity = (double)gpu.TotalMiB;
        const string caveat = "This is a general recommendation, not a measurement of current free VRAM or a compatibility guarantee. Other GPU apps and image input can increase memory needs. No settings are changed and no model is loaded by this check.";
        if (fileMiB > capacity)
            return new(MemoryFitLevel.ExceedsCapacity, "Not recommended",
                "The model alone is too large for this GPU. Choose a smaller model. " + caveat);

        if (!TryEstimateWorkingSet(model, model.RecommendedProfile, out var workingMiB, out var kvSlope))
            return new(MemoryFitLevel.Unknown, "",
                "A memory recommendation is not available for this model/profile. Missing metadata does not mean memory headroom is limited.");

        var minimumMiB = workingMiB + kvSlope * MinimumContextTokens / 1024d;
        if (minimumMiB > capacity)
            return new(MemoryFitLevel.ExceedsCapacity, "Not recommended",
                "The model and a reduced context are estimated to exceed this GPU's memory. Choose a smaller model. " + caveat);

        var referenceMiB = workingMiB + kvSlope * ReferenceContextTokens / 1024d;
        var comfortable = referenceMiB <= capacity * .80 && capacity - referenceMiB >= 2048;
        return new(comfortable ? MemoryFitLevel.Comfortable : MemoryFitLevel.Close,
            comfortable ? "Fits" : "Close",
            (comfortable ? "The model and a useful context are estimated to fit comfortably. Larger contexts may need more memory. "
                : "Memory headroom is limited. A smaller context may be needed; larger contexts are not recommended. ") + caveat);
    }

    private static double ArtifactMiB(ModelInfo model) =>
        Math.Max(model.SizeBytes, model.MemoryGeometry?.ArtifactBytes ?? 0) / (1024d * 1024d);

    private static bool TryEstimateWorkingSet(ModelInfo model, ModelProfile profile, out double workingMiB, out double kvSlope, bool fit = false)
    {
        var fileMiB = ArtifactMiB(model);
        var matchingProfile = model.EstimateKvPrecision == profile.KvPrecision
            && (model.EstimateSpeculativeMode == profile.SpeculativeMode || profile.SpeculativeMode == SpeculativeMode.Disabled);
        var catalogEstimate = model.EstimatedBaseVramMiB > 0 && matchingProfile;
        var geometrySlope = 0d;
        var draftMiB = 0d;
        var geometryEstimate = model.MemoryGeometry?.TryEstimate(profile, out geometrySlope, out draftMiB) == true;
        if (fit) geometrySlope /= ModelMemoryGeometry.RecommendationKvAllowance;
        var overhead = fit ? Math.Max(1536, fileMiB * .065) : Math.Max(2048, fileMiB * .10);
        workingMiB = catalogEstimate ? Math.Max(fileMiB, model.EstimatedBaseVramMiB)
            : fileMiB + overhead + draftMiB
                + (profile.SpeculativeMode == SpeculativeMode.Disabled ? 0 : 512);
        var contextKnown = catalogEstimate && double.IsFinite(model.EstimatedKvMiBPerKToken)
            && model.EstimatedKvMiBPerKToken > 0;
        kvSlope = contextKnown ? Math.Max(model.EstimatedKvMiBPerKToken, geometrySlope) : geometrySlope;
        return contextKnown || geometryEstimate;
    }
}
