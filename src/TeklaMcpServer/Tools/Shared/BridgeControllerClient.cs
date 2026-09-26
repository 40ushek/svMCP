using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SvMcp.Bridge;

namespace TeklaMcpServer.Tools;

internal sealed class BridgeControllerClient
{
    private static int _nextId;
    private static readonly Mutex RequestMutex = new(false, BridgeControllerProtocol.RequestMutexName);

    public string Execute(string command, string[] args, TimeSpan timeout)
    {
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = RequestMutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // The previous MCP process exited while it owned the bridge request slot.
                ownsMutex = true;
            }

            var response = Send(new BridgeControllerRequest
            {
                Operation = "execute",
                Command = command,
                Args = args,
                TimeoutMilliseconds = checked((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue))
            }, timeout);
            return response.Result ?? string.Empty;
        }
        finally
        {
            if (ownsMutex)
                RequestMutex.ReleaseMutex();
        }
    }

    private static BridgeControllerResponse Send(BridgeControllerRequest request, TimeSpan responseTimeout)
    {
        request.Id = Interlocked.Increment(ref _nextId);
        var controllerPath = Path.Combine(AppContext.BaseDirectory, "controller", "TeklaBridge.Controller.exe");
        if (!File.Exists(controllerPath))
            throw new FileNotFoundException("svMCP tray controller was not found. Rebuild or redeploy svMCP.", controllerPath);

        Exception? lastError = null;
        var deadline = Stopwatch.StartNew();
        var controllerStarted = false;
        var requestMayHaveBeenSent = false;
        while (deadline.Elapsed < TimeSpan.FromSeconds(12))
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    BridgeControllerProtocol.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.Connect(350);

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                requestMayHaveBeenSent = true;
                writer.WriteLine(JsonSerializer.Serialize(request, BridgeControllerProtocol.JsonOptions));
                var readTask = reader.ReadLineAsync();
                var responseLine = readTask.WaitAsync(responseTimeout + TimeSpan.FromSeconds(15)).GetAwaiter().GetResult()
                    ?? throw new EndOfStreamException("Controller closed the pipe before replying.");
                var response = JsonSerializer.Deserialize<BridgeControllerResponse>(responseLine, BridgeControllerProtocol.JsonOptions)
                    ?? throw new InvalidDataException("Controller returned an invalid response.");
                if (response.Id != request.Id)
                    throw new InvalidDataException("Controller response id did not match the request.");
                if (!response.Ok)
                    throw new InvalidOperationException(response.Error ?? "Controller request failed.");
                return response;
            }
            catch (TimeoutException ex)
            {
                if (requestMayHaveBeenSent)
                    throw new InvalidOperationException("The controller stopped responding after the request was sent; the operation result is unknown and was not retried.", ex);
                lastError = ex;
            }
            catch (IOException ex)
            {
                if (requestMayHaveBeenSent)
                    throw new InvalidOperationException("The controller connection was lost after the request was sent; the operation result is unknown and was not retried.", ex);
                lastError = ex;
            }

            if (!controllerStarted)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(controllerPath)
                    {
                        WorkingDirectory = Path.GetDirectoryName(controllerPath)!,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                controllerStarted = true;
            }

            Thread.Sleep(150);
        }

        throw new InvalidOperationException("Could not connect to the svMCP tray controller.", lastError);
    }
}
