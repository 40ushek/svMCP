using System;
using System.IO;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingCaseSnapshotReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DrawingCaseSnapshot Load(string caseDirectory)
    {
        if (string.IsNullOrWhiteSpace(caseDirectory))
            throw new ArgumentException("Case directory is required.", nameof(caseDirectory));

        var beforePath = Path.Combine(caseDirectory, "before.json");
        var afterPath = Path.Combine(caseDirectory, "after.json");
        var metaPath = Path.Combine(caseDirectory, "meta.json");

        return new DrawingCaseSnapshot
        {
            CaseDirectory = caseDirectory,
            BeforePath = beforePath,
            AfterPath = afterPath,
            MetaPath = metaPath,
            Before = ReadJson<DrawingContext>(beforePath),
            After = ReadJson<DrawingContext>(afterPath),
            Meta = ReadJson<DrawingCaseMeta>(metaPath)
        };
    }

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Drawing case file was not found.", path);

        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        return value ?? throw new InvalidOperationException($"Drawing case file '{path}' could not be read.");
    }
}

internal sealed class DrawingCaseSnapshot
{
    public string CaseDirectory { get; set; } = string.Empty;

    public string BeforePath { get; set; } = string.Empty;

    public string AfterPath { get; set; } = string.Empty;

    public string MetaPath { get; set; } = string.Empty;

    public DrawingContext Before { get; set; } = new();

    public DrawingContext After { get; set; } = new();

    public DrawingCaseMeta Meta { get; set; } = new();
}
