namespace NInferManager.Contracts;

public enum MemoryFitLevel { Unknown, Comfortable, Close, ExceedsCapacity }

public sealed record ModelMemoryFit(MemoryFitLevel Level, string Label, string Explanation)
{
    public const int ReferenceContextTokens = 65536;
    public const int MinimumContextTokens = 32768;

    public static ModelMemoryFit Assess(ModelInfo model, GpuStatus? gpu)
    {
        if (gpu is null || gpu.TotalMiB <= 0 || model.SizeBytes <= 0
            || model.RecommendedProfile.Device != gpu.DeviceIndex)
            return new(MemoryFitLevel.Unknown, "", "A recommendation needs model size and memory information for the selected GPU.");

        var fileMiB = Math.Max(model.SizeBytes, model.MemoryGeometry?.ArtifactBytes ?? 0) / (1024d * 1024d);
        var capacity = (double)gpu.TotalMiB;
        const string caveat = "This is a general recommendation, not a measurement of current free VRAM or a compatibility guarantee. Other GPU apps and image input can increase memory needs. No settings are changed and no model is loaded by this check.";
        if (fileMiB > capacity)
            return new(MemoryFitLevel.ExceedsCapacity, "Not recommended",
                "The model alone is too large for this GPU. Choose a smaller model. " + caveat);

        var p = model.RecommendedProfile;
        var matchingProfile = model.EstimateKvPrecision == p.KvPrecision
            && (model.EstimateSpeculativeMode == p.SpeculativeMode || p.SpeculativeMode == SpeculativeMode.Disabled);
        var catalogEstimate = model.EstimatedBaseVramMiB > 0 && matchingProfile;
        var geometrySlope = 0d; var draftMiB = 0d;
        var geometryEstimate = model.MemoryGeometry?.TryEstimate(p, out geometrySlope, out draftMiB) == true;
        var workingMiB = catalogEstimate ? Math.Max(fileMiB, model.EstimatedBaseVramMiB)
            : fileMiB + Math.Max(2048, fileMiB * .10) + draftMiB
                + (p.SpeculativeMode == SpeculativeMode.Disabled ? 0 : 512);
        var contextKnown = catalogEstimate && double.IsFinite(model.EstimatedKvMiBPerKToken)
            && model.EstimatedKvMiBPerKToken > 0;
        if (!contextKnown && !geometryEstimate)
            return new(MemoryFitLevel.Unknown, "",
                "A memory recommendation is not available for this model/profile. Missing metadata does not mean memory headroom is limited.");

        var kvSlope = contextKnown ? model.EstimatedKvMiBPerKToken : geometrySlope;
        if (geometryEstimate) kvSlope = Math.Max(kvSlope, geometrySlope);

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
}
