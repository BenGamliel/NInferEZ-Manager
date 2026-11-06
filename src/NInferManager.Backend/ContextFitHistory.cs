using System.Security.Cryptography;
using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

internal sealed record ContextFitSample(int Tokens, int FreeMiB);

internal sealed class ContextFitHistory(string dataDirectory)
{
    private readonly string _path = Path.Combine(dataDirectory, "cache", "context-fit.json");
    private Dictionary<string, ContextFitSample>? _samples;

    public ContextFitSample? Find(string key)
    {
        _samples ??= Read();
        return _samples.TryGetValue(key, out var sample) && sample is { Tokens: >= 16384 and <= 1048576, FreeMiB: >= 0 }
            ? sample : null;
    }

    public void Remember(string key, ContextFitSample sample)
    {
        _samples ??= Read();
        _samples.Remove(key);
        _samples.Add(key, sample);
        if (_samples.Count > 128) _samples.Remove(_samples.Keys.First());

        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(_samples));
            File.Move(temporary, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private Dictionary<string, ContextFitSample> Read()
    {
        try
        {
            var file = new FileInfo(_path);
            if (file.Exists && file.Length <= 65536)
            {
                var samples = JsonSerializer.Deserialize<Dictionary<string, ContextFitSample>>(File.ReadAllBytes(_path));
                if (samples is { Count: <= 128 }) return samples;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }

    public static string Key(string modelPath, string enginePath, GpuStatus gpu, ModelProfile profile, bool ropeScaling)
    {
        var model = new FileInfo(modelPath);
        var engine = new FileInfo(enginePath);
        var options = ProfileSettingsPolicy.Clone(profile);
        options.CustomKvCapacity = options.DefaultMaxTokens = 0;
        options.ManualAdvancedSettings = null;
        var identity = new
        {
            Model = new { model.FullName, model.Length, model.LastWriteTimeUtc },
            Engine = new { engine.FullName, engine.Length, engine.LastWriteTimeUtc },
            Gpu = new { gpu.Uuid, gpu.Name, gpu.TotalMiB, gpu.DeviceIndex },
            Profile = options,
            RopeScaling = ropeScaling
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
    }
}
