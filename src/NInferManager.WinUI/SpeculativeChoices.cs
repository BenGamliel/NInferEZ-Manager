using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed record SpeculativeChoice(SpeculativeMode Mode, string Label, bool ConfirmedSupported);

public static class SpeculativeChoices
{
    public static IReadOnlyList<SpeculativeChoice> ForModel(ModelInfo model, SpeculativeMode configuredMode, bool managed = false)
    {
        var choices = new List<SpeculativeChoice> { new(SpeculativeMode.Disabled, "Disabled", true) };
        foreach (var mode in new[] { SpeculativeMode.Mtp, SpeculativeMode.Dflash, SpeculativeMode.Dflash2 })
        {
            var supported = model.Inspected && (mode switch
            {
                SpeculativeMode.Mtp => model.HasMtp,
                SpeculativeMode.Dflash => model.HasDflash,
                SpeculativeMode.Dflash2 => model.HasDflash2,
                _ => false
            });
            // Preserve an imported/current setting even when metadata rejects it.
            // Removing the current option would silently change Exact behavior.
            if (!supported && (mode != configuredMode || managed && model.Inspected)) continue;
            var name = mode switch { SpeculativeMode.Mtp => "MTP", SpeculativeMode.Dflash => "DFlash", _ => "DFlash2" };
            choices.Add(new(mode, supported ? name : name + (model.Inspected ? " · unsupported by artifact" : " · metadata unknown"), supported));
        }
        return choices;
    }
}
