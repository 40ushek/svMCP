namespace TeklaMcpServer.Api.Connection;

public sealed class ConnectionInfo
{
    public bool IsConnected { get; set; }

    public string? ModelName { get; set; }

    public string? ModelPath { get; set; }
}
