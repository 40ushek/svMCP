using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SvMcp.Shared.Logging;

internal sealed class SvMcpLogRouter
{
    private const long MaxLogBytes = 4L * 1024 * 1024;
    private readonly BoundedFileLogWriter _performanceLog = new(@"C:\temp\svmcp-perf.log", MaxLogBytes);
    private readonly BoundedFileLogWriter _viewLayoutLog = new(@"C:\temp\svmcp-view-layout.log", MaxLogBytes);

    public void Append(string layer, string operation, string line)
    {
        var writer = IsViewLayoutEntry(layer, operation)
            ? _viewLayoutLog
            : _performanceLog;

        writer.AppendLine(line);
    }

    private static bool IsViewLayoutEntry(string layer, string operation)
        => string.Equals(layer, "api-view", StringComparison.Ordinal)
            || string.Equals(operation, "fit_views_to_sheet", StringComparison.Ordinal)
            || string.Equals(operation, "arrange_views_only", StringComparison.Ordinal);
}

internal sealed class BoundedFileLogWriter
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly Mutex _mutex;

    public BoundedFileLogWriter(string path, long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Log path is required.", nameof(path));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        _path = path;
        _maxBytes = maxBytes;
        _mutex = new Mutex(false, BuildMutexName(path));
    }

    public void AppendLine(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? Path.GetTempPath());
            WriteWithSizeLimit(line + Environment.NewLine);
        }
        catch
        {
            // Logging must not interrupt the caller.
        }
    }

    private void WriteWithSizeLimit(string text)
    {
        var mutexAcquired = false;
        try
        {
            try
            {
                mutexAcquired = _mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                mutexAcquired = true;
            }

            if (!mutexAcquired)
                return;

            var bytes = Encoding.UTF8.GetBytes(text);
            using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (stream.Length + bytes.Length > _maxBytes)
                stream.SetLength(0);

            stream.Position = stream.Length;
            stream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            if (mutexAcquired)
                _mutex.ReleaseMutex();
        }
    }

    private static string BuildMutexName(string path)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
        return @"Local\svMCP.FileLog." + BitConverter.ToString(hash).Replace("-", string.Empty);
    }
}
