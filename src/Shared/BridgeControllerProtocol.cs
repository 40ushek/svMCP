using System.Text.Json;

namespace SvMcp.Bridge;

public static class BridgeControllerProtocol
{
    public const string PipeName = "svMcpTeklaBridgeController";
    public const string RequestMutexName = @"Local\svMcpTeklaBridgeRequests";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class BridgeControllerRequest
{
    public int Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? Command { get; set; }
    public string[] Args { get; set; } = [];
    public int TimeoutMilliseconds { get; set; }
}

public sealed class BridgeControllerResponse
{
    public int Id { get; set; }
    public bool Ok { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public BridgeControllerStatus? Status { get; set; }
}

public sealed class BridgeControllerStatus
{
    public string State { get; set; } = "Stopped";
    public bool BridgeRunning { get; set; }
    public bool Paused { get; set; }
    public string TeklaConnection { get; set; } = "Unknown";
    public string? Error { get; set; }
}
