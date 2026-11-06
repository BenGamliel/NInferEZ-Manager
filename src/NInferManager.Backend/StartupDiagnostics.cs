using System.Globalization;
using System.Text.RegularExpressions;

namespace NInferManager.Backend;

public static class StartupDiagnostics
{
    private static readonly Regex ContextCapacityNumbers = new(
        @"Engine runtime reservation requires (?<required>[0-9]+) bytes(?: in addition to (?<reserve>[0-9]+) bytes of automatic headroom)?, but only (?<available>[0-9]+) bytes",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static bool IsContextCapacityFailure(string detail) =>
        detail.Contains("Engine runtime reservation requires", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("automatic KV headroom requires", StringComparison.OrdinalIgnoreCase)
        || (detail.Contains("CUDA", StringComparison.OrdinalIgnoreCase)
            && detail.Contains("out of memory", StringComparison.OrdinalIgnoreCase));

    internal static long ContextCapacityShortfall(string detail)
    {
        var match = ContextCapacityNumbers.Match(detail);
        if (!match.Success
            || !long.TryParse(match.Groups["required"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var required)
            || !long.TryParse(match.Groups["available"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var available))
            return 0;
        long.TryParse(match.Groups["reserve"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var reserve);
        return required <= long.MaxValue - reserve ? Math.Max(0, required + reserve - available) : 0;
    }

    public static string UserMessage(string detail)
    {
        if (detail.Contains("runtime reservation", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
            return "There is not enough free GPU memory for this model and its working buffers. Close other GPU applications, use automatic context fitting, or reduce Context limit and Prompt processing batch. If model weights alone do not fit, choose a smaller model. Full details are in Logs.";
        if (detail.Contains("--media-live-mib", StringComparison.OrdinalIgnoreCase))
            return "Live image budget must be at least 1 MiB when image input is enabled. Restore the recommended profile or increase this setting under Vision. Full details are in Logs.";
        if (detail.Contains("--mtp-attention-window", StringComparison.OrdinalIgnoreCase))
            return "MTP attention window applies only to MTP. Choose MTP and set this window to 0 (model default) or at least Draft tokens + 1. You can also enable Manage advanced settings for me.";
        if (detail.Contains("--lm-head-draft", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("--draft-tokens", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("--ngram-draft-tokens", StringComparison.OrdinalIgnoreCase))
            return "The acceleration settings do not match the selected mode or model. Choose an acceleration mode reported by this model, or Disabled, then restore recommended defaults or enable Manage advanced settings for me. Full details are in Logs.";
        if (detail.Contains("--use-alt-prefix-caching", StringComparison.OrdinalIgnoreCase))
            return "Alternative prefix caching needs prefix reuse and cannot use the default cache's state and continuation settings. Enable prefix reuse or turn off Alternative prefix caching. Full details are in Logs.";
        if (detail.Contains("--prefill-chunk", StringComparison.OrdinalIgnoreCase))
            return "Prompt processing batch must be a positive multiple of 128. Use 2048 or enable Manage advanced settings for me.";
        if (detail.Contains("--kv-capacity", StringComparison.OrdinalIgnoreCase))
            return "Custom KV capacity must be at least Context limit. Choose Match context, increase the custom capacity, or enable Manage advanced settings for me.";
        if (detail.Contains("--kv-headroom-mib", StringComparison.OrdinalIgnoreCase))
            return "KV memory reserve applies only to automatic KV capacity. Choose Auto for KV capacity policy, or restore recommended defaults.";
        if (detail.Contains("NInfer exited during startup", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("ninfer-serve:", StringComparison.OrdinalIgnoreCase))
            return "The engine could not finish loading this model. Check that the selected engine supports your GPU and model, then restore recommended defaults. Open Logs for the specific engine error.";
        return detail;
    }

    public static bool IsFailure(string line) => line.StartsWith("ninfer-serve:", StringComparison.OrdinalIgnoreCase)
        || line.Contains(" FATAL ", StringComparison.OrdinalIgnoreCase)
        || line.Contains(" ERROR ", StringComparison.OrdinalIgnoreCase);

    public static string Summarize(IEnumerable<string> lines)
    {
        var all = lines.ToArray();
        var failures = all.Where(IsFailure).OrderByDescending(x => x.Contains(" FATAL ", StringComparison.OrdinalIgnoreCase)).Distinct().Take(3).ToArray();
        if (failures.Length != 0) return string.Join(" | ", failures);
        return all.Length == 0 ? "Open Logs for startup details." : string.Join(" | ", all.TakeLast(3));
    }
}
