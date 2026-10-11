namespace NInferManager.Contracts;

public sealed record ConversionRequest
{
    public string Source { get; init; } = "";
    public string Output { get; init; } = "";
    public string Profile { get; init; } = "auto";
    public string? ConfigFolder { get; init; }
    public string? ConfigFile { get; init; }
    public string? ResourceFolder { get; init; }
    public string[] Components { get; init; } = ["text"];
    public Dictionary<string,string> Sources { get; init; } = [];
    public Dictionary<string,string> Resources { get; init; } = [];
    public Dictionary<string,string> ShardOverrides { get; init; } = [];
    public ConversionTensorOverride[] TensorOverrides { get; init; } = [];
    public string? Recipe { get; init; }
    public string? Override { get; init; }
    public string Device { get; init; } = "cpu";
    public int RowsPerChunk { get; init; } = 512;
    public long MaxFileBytes { get; init; } = 32_000_000_000;
    public string? Name { get; init; }
    public bool Proposal { get; init; }
    public int ProposalRows { get; init; } = 131_072;
    public string? Ranking { get; init; }
    public string? ValidateEngine { get; init; }
    public int ValidateTimeoutSeconds { get; init; } = 600;
    public bool AllowCustomCode { get; init; }
}

public sealed record ConversionTensorOverride
{
    public string Selector { get; init; } = "";
    public string Format { get; init; } = "q8_g32_fp16";
    public string Method { get; init; } = "grouped_absmax";
    public string Layout { get; init; } = "auto";
    public string ActivationPolicy { get; init; } = "";
    public Dictionary<string,System.Text.Json.JsonElement> Parameters { get; init; } = [];
}

public sealed record ConversionCapabilities(string Version, string[] Profiles, string[] Recipes,
    string[] Components, string[] Inputs, string[] Devices)
{
    public string[] Formats { get; init; } = [];
    public string[] Methods { get; init; } = [];
    public string[] ResourceRoles { get; init; } = [];
}

public sealed record ConversionStatus(string JobId, string Command, bool Running, bool Cancelling,
    string Phase, double? Percent, long WrittenBytes, long TotalBytes, string Message,
    bool CanConvert, string? Plan, string? Output, string? Report, bool RuntimeVerified);
