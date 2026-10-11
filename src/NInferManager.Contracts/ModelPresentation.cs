namespace NInferManager.Contracts;

public static class ModelPresentation
{
    public static string WeightSummary(string formats, string fileName)
    {
        if (string.IsNullOrWhiteSpace(formats)) return "NInfer v3";
        if (!formats.Contains(',') && formats.Length <= 48) return formats;
        var filename = fileName.Replace('-', '_');
        foreach (var format in formats.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).OrderByDescending(x => x.Length))
        {
            var quant = format.StartsWith("gguf_", StringComparison.OrdinalIgnoreCase) ? format[5..] : format;
            if (quant is "bf16" or "fp32" or "int32") continue;
            if (filename.Contains(quant, StringComparison.OrdinalIgnoreCase)) return quant.ToUpperInvariant();
        }
        return "Mixed quantization";
    }
}
