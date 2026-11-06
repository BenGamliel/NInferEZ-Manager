using System.Text.Json;

namespace NInferManager.Backend;

/// <summary>Release versions are numeric x.y.z. Preview builds never satisfy a stable minimum.</summary>
public static class ModelVersionPolicy
{
    public static bool IsValidMinimum(string? value) => string.IsNullOrEmpty(value) || TryStable(value, out _);
    public static bool Meets(string? current, string? minimum) => string.IsNullOrEmpty(minimum) ||
        TryStable(minimum, out var required) && TryStable(current, out var actual) && actual >= required;
    private static bool TryStable(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        return value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")
            && Version.TryParse(value, out version!);
    }
    public static string? Requirement(ModelCatalogEntry model, string? engineVersion = null, bool includeEngine = false)
    {
        if (!IsValidMinimum(model.MinimumManagerVersion) || !IsValidMinimum(model.MinimumEngineVersion))
            return "This model has invalid version requirements. Refresh the catalog before continuing.";
        if (!Meets(ProductInfo.Version, model.MinimumManagerVersion))
            return $"Update NInferEZ Manager to {model.MinimumManagerVersion} or later to download and run this model.";
        if (includeEngine && !Meets(engineVersion, model.MinimumEngineVersion))
            return $"This model requires NInferEZ {model.MinimumEngineVersion} or later. Update NInferEZ Manager to install the matching version.";
        return null;
    }
    public static string Stronger(string current, string incoming)
    {
        if(!IsValidMinimum(current)||!IsValidMinimum(incoming))throw new InvalidDataException("Invalid model version requirement.");
        if(string.IsNullOrEmpty(current))return incoming;
        return !string.IsNullOrEmpty(incoming) && Version.Parse(incoming)>Version.Parse(current) ? incoming : current;
    }
    public static void Require(ModelCatalogEntry model, string? engineVersion = null, bool includeEngine = false)
    {
        if (Requirement(model, engineVersion, includeEngine) is { } message) throw new InvalidOperationException(message);
    }
    public static string? ReadEngineVersion(string engineDirectory)
    {
        try
        {
            var path = Path.Combine(engineDirectory, "engine-manifest.json");
            if (new FileInfo(path).Length > 65536) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("engineVersion", out var version) ? version.GetString() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }
}
