using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Tests;

/// <summary>
/// Reads a captured drawing state from `cases/dimension_cases/assembly/&lt;guid&gt;/&lt;state&gt;/`
/// back into the same read models the bridge produces, so the detector can be graded offline
/// against real drawings without Tekla.
///
/// The states on disk are not one schema: `get_dimension_contexts` serializes PascalCase while
/// part geometry and coverage serialize camelCase. Case-insensitive matching absorbs that — but
/// silently, so the loader reports counts and the tests assert on them. A file that parsed into
/// an empty object would otherwise look exactly like a drawing with nothing in it.
/// </summary>
public sealed class DimensionCaseState
{
    public string Guid { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ViewId { get; init; }
    public List<DimensionContextInfo> Dimensions { get; init; } = new();
    public List<PartGeometryInViewResult> Parts { get; init; } = new();
    public List<DimensionChainCoverageResult> Coverage { get; init; } = new();

    /// <summary>
    /// Set when the capture on disk is a saved failure rather than a state: two of them exist,
    /// where the bridge threw and the error payload was written to the state file. Such a state
    /// must not be graded, and must not be mistaken for a drawing with nothing on it.
    /// </summary>
    public string? LoadError { get; init; }

    public bool IsUsable => LoadError == null && Dimensions.Count > 0 && Parts.Count > 0;
    public bool HasCoverage => Coverage.Count > 0;
    public override string ToString() => $"{Guid[..8]}/{State}";
}

public static class DimensionCaseLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string CasesRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "cases", "dimension_cases", "assembly");
                if (Directory.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// `cases/` is gitignored — it holds real drawings from the plant and is not something this
    /// repository can ship. On a clean clone it does not exist, and tests that grade the detector
    /// against it have to say so and pass rather than fail: a missing local corpus is not a
    /// regression, and CI would otherwise be permanently red for a reason no change here can fix.
    /// </summary>
    public static bool HasCases =>
        !string.IsNullOrEmpty(CasesRoot) && Directory.EnumerateDirectories(CasesRoot).Any();

    public static IEnumerable<DimensionCaseState> LoadAll()
    {
        var root = CasesRoot;
        if (string.IsNullOrEmpty(root))
            yield break;

        foreach (var drawing in Directory.GetDirectories(root).OrderBy(static path => path))
            foreach (var state in Directory.GetDirectories(drawing).OrderBy(static path => path))
            {
                var loaded = Load(Path.GetFileName(drawing), state);
                if (loaded != null)
                    yield return loaded;
            }
    }

    public static DimensionCaseState? Load(string guid, string stateDirectory)
    {
        var contextsPath = Path.Combine(stateDirectory, "dimension_contexts.json");
        var partsPath = Path.Combine(stateDirectory, "parts_geometry.json");
        if (!File.Exists(contextsPath) || !File.Exists(partsPath))
            return null;

        var contextsJson = File.ReadAllText(contextsPath);
        var capturedError = ReadCapturedError(contextsJson) ?? ReadCapturedError(File.ReadAllText(partsPath));
        if (capturedError != null)
            return new DimensionCaseState { Guid = guid, State = Path.GetFileName(stateDirectory), LoadError = capturedError };

        var contexts = JsonSerializer.Deserialize<GetDimensionContextsResult>(contextsJson, Options);
        var parts = JsonSerializer.Deserialize<PartsGeometryFile>(File.ReadAllText(partsPath), Options);

        var coverage = Directory.GetFiles(stateDirectory, "coverage_*.json")
            .OrderBy(static path => path)
            .Select(path => JsonSerializer.Deserialize<DimensionChainCoverageResult>(File.ReadAllText(path), Options))
            .Where(static result => result != null)
            .Select(static result => result!)
            .ToList();

        return new DimensionCaseState
        {
            Guid = guid,
            State = Path.GetFileName(stateDirectory),
            ViewId = contexts?.ViewId ?? 0,
            Dimensions = contexts?.Dimensions ?? new List<DimensionContextInfo>(),
            Parts = parts?.Parts ?? new List<PartGeometryInViewResult>(),
            Coverage = coverage
        };
    }

    /// <summary>
    /// A bridge failure serializes as {"error": ..., "type": ...} into the same file name a state
    /// uses. Deserializing it yields an object with every list empty, which is indistinguishable
    /// from a clean drawing — so detect it explicitly instead.
    /// </summary>
    private static string? ReadCapturedError(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : null;
    }

    private sealed class PartsGeometryFile
    {
        public List<PartGeometryInViewResult> Parts { get; set; } = new();
    }
}
