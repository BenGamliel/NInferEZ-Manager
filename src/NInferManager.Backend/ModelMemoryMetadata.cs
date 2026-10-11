using System.Buffers.Binary;
using System.Text.Json;
using NInferManager.Contracts;

namespace NInferManager.Backend;

public static class ModelMemoryMetadata
{
    public const int MaximumDirectoryBytes = 8 * 1024 * 1024;

    public static ModelMemoryGeometry? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[32];
            stream.ReadExactly(header);
            if (!header[..8].SequenceEqual("NINFER\0\x03"u8)) return null;
            var length = BinaryPrimitives.ReadUInt64LittleEndian(header[8..16]);
            if (length == 0 || length > MaximumDirectoryBytes || length > (ulong)Math.Max(0, stream.Length - 32)) return null;
            var directory = new byte[(int)length];
            stream.ReadExactly(directory);
            using var document = JsonDocument.Parse(directory, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            var components = root.GetProperty("components");
            if (components.ValueKind != JsonValueKind.Object) return null;
            JsonElement target = default;
            foreach (var component in components.EnumerateObject())
            {
                if (component.Name is not ("text" or "target" or "model")) continue;
                if (!component.Value.TryGetProperty("config", out var config)) continue;
                if (!config.TryGetProperty("model_type", out var type)
                    || type.GetString() is not ("qwen3_5_text" or "qwen3_5_moe_text")) continue;
                if (target.ValueKind != JsonValueKind.Undefined) return null;
                target = config;
            }
            if (target.ValueKind == JsonValueKind.Undefined) return null;
            var targetGeometry = Geometry(target, false);
            if (targetGeometry is null) return null;
            var files = root.GetProperty("files");
            if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is < 1 or > 4096) return null;
            long payloadBytes = 0;
            foreach (var file in files.EnumerateArray())
            {
                var bytes = file.GetProperty("payload_bytes").GetInt64();
                if (bytes < 0) return null;
                payloadBytes = checked(payloadBytes + bytes);
            }
            var entryHeaderBytes = ((32L + (long)length + 4095) / 4096) * 4096;
            if (checked(entryHeaderBytes + files[0].GetProperty("payload_bytes").GetInt64()) != stream.Length) return null;
            var artifactBytes = checked(payloadBytes + entryHeaderBytes + (files.GetArrayLength() - 1L) * 4096);
            if (artifactBytes < stream.Length) return null;
            AttentionMemoryGeometry? Draft(string name) => components.TryGetProperty(name, out var component)
                && component.TryGetProperty("config", out var config) ? Geometry(config, true) : null;
            return new(artifactBytes, targetGeometry, components.TryGetProperty("mtp", out _), Draft("dflash"), Draft("dflash2"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { return null; }
    }

    private static AttentionMemoryGeometry? Geometry(JsonElement config, bool draft)
    {
        var layers = config.GetProperty("layer_types");
        var count = config.GetProperty("num_hidden_layers").GetInt32();
        if (layers.ValueKind != JsonValueKind.Array || count is < 1 or > 1024 || layers.GetArrayLength() != count) return null;
        var full = 0; var local = 0;
        foreach (var layer in layers.EnumerateArray())
        {
            switch (layer.GetString())
            {
                case "full_attention": full++; break;
                case "linear_attention" when !draft: break;
                case "sliding_attention" when draft: local++; break;
                default: return null;
            }
        }
        var heads = config.GetProperty("num_key_value_heads").GetInt32();
        var dimension = config.GetProperty("head_dim").GetInt32();
        var window = local > 0 ? config.GetProperty("sliding_window").GetInt32() : 0;
        if (heads is < 1 or > 256 || dimension is < 1 or > 1024 || (local > 0 && window is < 1 or > 1048576)) return null;
        return new(full, local, heads, dimension, window);
    }
}
