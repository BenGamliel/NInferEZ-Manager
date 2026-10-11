namespace NInferManager.Contracts;

public sealed record AttentionMemoryGeometry(int FullLayers, int LocalLayers, int KvHeads, int HeadDimension, int LocalWindow);

public sealed record ModelMemoryGeometry(long ArtifactBytes, AttentionMemoryGeometry Target,
    bool HasMtp, AttentionMemoryGeometry? Dflash, AttentionMemoryGeometry? Dflash2)
{
    public bool TryEstimate(ModelProfile profile, out double kvMiBPerKToken, out double fixedDraftMiB)
    {
        kvMiBPerKToken = fixedDraftMiB = 0;
        if (!Valid(Target)) return false;
        var vectorBytes = profile.KvPrecision == KvPrecision.Bf16 ? Target.HeadDimension * 4d
            : Target.HeadDimension != 256 ? double.NaN : profile.KvPrecision switch
            {
                KvPrecision.Int8 => 528,
                KvPrecision.Fp8 => 516,
                KvPrecision.Nvfp4 => 288,
                KvPrecision.K8v4 => 402,
                KvPrecision.Rk8v4 => 408,
                _ => double.NaN
            };
        if (!double.IsFinite(vectorBytes)) return false;
        var targetLayers = Target.FullLayers;
        if (profile.SpeculativeMode == SpeculativeMode.Mtp)
        {
            if (!HasMtp) return false;
            targetLayers++;
        }
        var bytesPerToken = targetLayers * (double)Target.KvHeads * vectorBytes;
        if (profile.SpeculativeMode is SpeculativeMode.Dflash or SpeculativeMode.Dflash2)
        {
            var draft = profile.SpeculativeMode == SpeculativeMode.Dflash ? Dflash : Dflash2;
            if (draft is null || !Valid(draft)) return false;
            // NInfer's masked draft caches use BF16, independently of the target KV dtype.
            var draftVectorBytes = draft.KvHeads * (double)draft.HeadDimension * 4;
            bytesPerToken += draft.FullLayers * draftVectorBytes;
            fixedDraftMiB = draft.LocalLayers * draftVectorBytes * draft.LocalWindow / (1024d * 1024d);
        }
        // Small allowance for paged-cache tables/alignment, not an exact runtime reservation.
        kvMiBPerKToken = bytesPerToken / 1024d * 1.10;
        return double.IsFinite(kvMiBPerKToken) && double.IsFinite(fixedDraftMiB);
    }

    private static bool Valid(AttentionMemoryGeometry geometry) => geometry.FullLayers is >= 0 and <= 1024
        && geometry.LocalLayers is >= 0 and <= 1024 && geometry.KvHeads is > 0 and <= 256
        && geometry.HeadDimension is > 0 and <= 1024
        && (geometry.LocalLayers == 0 || geometry.LocalWindow is > 0 and <= 1048576);
}
