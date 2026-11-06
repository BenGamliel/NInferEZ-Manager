using NInferManager.Contracts;

namespace NInferManager.Backend;

internal static class ContextFitSearch
{
    private const int UpgradeThreshold = 5000;
    private const int Step = ModelMemoryFit.FitContextStep;
    private const int Minimum = ModelMemoryFit.FitMinimumContextTokens;

    public static int InitialContext(int ceiling, int? estimate, double bytesPerToken, ContextFitSample? previous, int? freeMiB)
    {
        if (ceiling < Minimum)
            throw new InvalidOperationException("Set Context limit to at least 16,384 tokens to use automatic fitting.");
        if (previous is null || freeMiB is null)
            return ModelMemoryFit.RoundFitContext(estimate ?? ceiling, ceiling);

        var last = Math.Min(ceiling, previous.Tokens);
        var deltaMiB = freeMiB.Value - previous.FreeMiB;
        if (bytesPerToken <= 0)
        {
            if (estimate is not int estimated) return last;
            var next = ModelMemoryFit.RoundFitContext(estimated, ceiling);
            return last - next >= Step || next - last >= UpgradeThreshold ? next : last;
        }

        var deltaTokens = deltaMiB * 1048576d / bytesPerToken;
        var projected = ModelMemoryFit.RoundFitContext(previous.Tokens + deltaTokens, ceiling);
        if (deltaTokens <= -Step || projected - last >= UpgradeThreshold) return projected;
        return last;
    }

    public static async Task<int> FindAsync(int initial, double bytesPerToken, Func<int, Task<long?>> tryContext)
    {
        var tokens = initial;
        for (var failures = 0; ; failures++)
        {
            var shortfall = await tryContext(tokens);
            if (shortfall is null) return tokens;
            if (tokens == Minimum) return 0;

            var reduction = shortfall > 0 && bytesPerToken > 0 ? shortfall.Value / bytesPerToken : tokens / 4d;
            var minimumReduction = failures < 2 ? Step : Math.Max(Step, tokens / 8);
            tokens = ModelMemoryFit.RoundFitContext(tokens - Math.Max(minimumReduction, reduction), tokens);
        }
    }
}
