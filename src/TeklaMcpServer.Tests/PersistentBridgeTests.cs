using System.Text.Json;
using TeklaMcpServer.Tools;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class PersistentBridgeTests
{
    [Fact]
    public void SendReturnsPayloadFromEchoProcess()
    {
        using var bridge = CreateBridge("echo", TimeSpan.FromSeconds(2), out _);
        var payload = bridge.Send("ping", "1");

        using var document = JsonDocument.Parse(payload);
        Assert.Equal("ping", document.RootElement.GetProperty("command").GetString());
        Assert.Equal("1", document.RootElement.GetProperty("arg0").GetString());
    }

    [Fact]
    public void SendRestartsProcessAfterFatalNotConnectedPayload()
    {
        using var bridge = CreateBridge("fatal-then-ok", TimeSpan.FromSeconds(2), out var stateFile);

        var firstPayload = bridge.Send("check_connection");
        using (var firstDocument = JsonDocument.Parse(firstPayload))
            Assert.Equal("Not connected to Tekla Structures", firstDocument.RootElement.GetProperty("error").GetString());

        var secondPayload = bridge.Send("check_connection");
        using (var secondDocument = JsonDocument.Parse(secondPayload))
            Assert.Equal("connected", secondDocument.RootElement.GetProperty("status").GetString());

        Assert.Equal("2", File.ReadAllText(stateFile).Trim());
    }

    [Fact]
    public void SendRestartsProcessAfterMalformedProtocolResponse()
    {
        using var bridge = CreateBridge("malformed-then-ok", TimeSpan.FromMilliseconds(500), out var stateFile);

        Assert.ThrowsAny<Exception>(() => bridge.Send("ping"));

        var payload = bridge.Send("ping");
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("recovered", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("2", File.ReadAllText(stateFile).Trim());
    }

    [Fact]
    public void SendRestartsProcessAfterResponseTimeout()
    {
        using var bridge = CreateBridge("timeout-then-ok", TimeSpan.FromSeconds(1), out var stateFile);

        Assert.Throws<TimeoutException>(() => bridge.Send("ping"));

        var payload = bridge.Send("ping");
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("recovered", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("2", File.ReadAllText(stateFile).Trim());
    }

    private static PersistentBridge CreateBridge(string mode, TimeSpan timeout, out string stateFile)
    {
        stateFile = Path.Combine(Path.GetTempPath(), $"svmcp-persistent-bridge-{Guid.NewGuid():N}.txt");
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "FakePersistentBridge.ps1");
        return new PersistentBridge(
            "powershell",
            AppContext.BaseDirectory,
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-Mode", mode, "-StateFile", stateFile],
            timeout);
    }
}
