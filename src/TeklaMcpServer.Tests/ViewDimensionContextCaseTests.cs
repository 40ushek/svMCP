using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using Xunit;
using Xunit.Abstractions;

namespace TeklaMcpServer.Tests;

/// <summary>
/// A view dimension context saved to a file and rebuilt from it must answer the same as
/// the original. The corpus test replays cases captured from real drawings: copy them from
/// %LOCALAPPDATA%\svMCP\DimensionContextCases into cases/dimension_context_cases at the
/// repository root. With no cases it passes and says so.
/// </summary>
public sealed class ViewDimensionContextCaseTests
{
    private readonly ITestOutputHelper _output;

    public ViewDimensionContextCaseTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("BackView", false)]
    [InlineData("BackView", true)]
    [InlineData("SectionView", false)]
    public void SavedCaseRebuildsAContextWithTheSameAnswers(string viewType, bool unread)
    {
        var original = viewType == "SectionView"
            ? DimensionChainPreviewTests.ISectionContext(endPlate: true)
            : DimensionChainPreviewTests.Context(170, viewType, upperPlate: true, unread: unread);
        var saved = original.ToCase();
        var directory = Path.Combine(Path.GetTempPath(), "svmcp-dimension-case-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = ViewDimensionContextCaseStore.Save(directory, saved);
            var replayed = ViewDimensionContextCaseStore.Load(path).ToContext();

            Assert.Equal(Answer(original.Query("chainDetails,parts,scale,diagnostics")),
                Answer(replayed.Query("chainDetails,parts,scale,diagnostics")));
            Assert.Equal(Answer(original.Query("points,edges")), Answer(replayed.Query("points,edges")));
            Assert.Equal(Answer(original.ChainPositions(verbose: true)), Answer(replayed.ChainPositions(verbose: true)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavedCaseKeepsTheReferenceZoneOutline()
    {
        var solids = new Dictionary<int, PartSolidGeometryInViewResult> {
            [1] = Rectangle(1, 0, 1200, 0, 3000), [10] = Rectangle(10, -50, 1250, -50, 3050) };
        var role = new PartRoleResult(PartRole.Included, "included", "test");
        var excluded = new PartRoleResult(PartRole.Excluded, "exclude-prefix:T", "test");
        var outline = new TeklaDrawingStructuralOutlineApi(
            new Roles(new(1, "S1", "S", role, zone: "3"), new(10, "T10", "T", excluded, zone: "0")),
            new Outlines(solids)).Get(7, referenceZones: ["0"]);
        var original = new ViewDimensionContext(7, 10, outline, [PartExclusionRule.ByPrefix("T")],
            new { drawingGuid = "test" }, new { viewType = "FrontView" });

        var json = JsonSerializer.Serialize(original.ToCase());
        var replayed = JsonSerializer.Deserialize<ViewDimensionContextCase>(json)!.ToContext();

        var answer = original.Query("chainDetails,parts,diagnostics", ruleSet: "panel");
        Assert.Equal("complete", answer.GetProperty("chainDiagnostics").GetProperty("referenceZone")
            .GetProperty("status").GetString());
        Assert.Equal(Answer(answer), Answer(replayed.Query("chainDetails,parts,diagnostics", ruleSet: "panel")));
    }

    [Fact]
    public void CasesOfOneViewWithDifferentExclusionsGetDifferentFiles()
    {
        var plain = ViewDimensionContextTests.Context().ToCase();
        var filtered = ViewDimensionContextTests.Context(exclusions: [PartExclusionRule.ByPrefix("R")]).ToCase();

        Assert.NotEqual(ViewDimensionContextCaseStore.GetFileName(plain), ViewDimensionContextCaseStore.GetFileName(filtered));
        Assert.StartsWith("test_7_", ViewDimensionContextCaseStore.GetFileName(plain));
    }

    [Fact]
    public void EveryCapturedCase_ReplaysTheSameWayTwice()
    {
        var directory = FindCorpusDirectory();
        var files = directory == null
            ? Array.Empty<string>()
            : Directory.GetFiles(directory, "*.json").OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        _output.WriteLine($"corpus={directory ?? "not found"} cases={files.Length}");

        foreach (var file in files)
        {
            var contextCase = ViewDimensionContextCaseStore.Load(file);
            var viewType = contextCase.Metadata.TryGetProperty("viewType", out var type) ? type.GetString() : null;
            // The panel rules cover elevations only; everything gets the steel rules.
            var ruleSets = viewType is "FrontView" or "BackView" ? new[] { "steel", "panel" } : new[] { "steel" };
            foreach (var ruleSet in ruleSets)
            {
                var first = Answer(contextCase.ToContext().Query("chain,scale", ruleSet: ruleSet));
                _output.WriteLine($"{Path.GetFileName(file)} [{viewType}] ruleSet={ruleSet}: {first}");
                Assert.Equal(first, Answer(ViewDimensionContextCaseStore.Load(file).ToContext().Query("chain,scale", ruleSet: ruleSet)));
            }
        }
    }

    // Each build issues its own context id; everything else must match.
    private static string Answer(JsonElement answer) =>
        System.Text.RegularExpressions.Regex.Replace(answer.GetRawText(), "ctx_[0-9a-f]{32}", "ctx");

    private static PartSolidGeometryInViewResult Rectangle(int id, double x0, double x1, double y0, double y1)
    {
        var geometry = new PartSolidGeometryInViewResult { Success = true, ModelId = id, ViewId = 7 };
        var points = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        for (var i = 0; i < points.Length; i++)
            geometry.Solid.Vertices.Add(new PartVertexGeometry { Index = i, Point = [points[i].Item1, points[i].Item2, 0] });
        var face = new PartFaceGeometry { Index = 0, Normal = [0, 0, 1] };
        face.Loops.Add(new PartLoopGeometry { Index = 0, VertexIndexes = [0, 1, 2, 3] });
        geometry.Solid.Faces.Add(face);
        return geometry;
    }

    private sealed class Roles(params PartRoleInView[] parts) : IDrawingPartRoleApi
    {
        public PartRoleReadResult GetRolesInView(int viewId) => new(parts, []);
    }

    private sealed class Outlines(Dictionary<int, PartSolidGeometryInViewResult> solids)
        : IDrawingViewOutlineApi, IDrawingPartSolidGeometryApi
    {
        public ViewAssemblyOutlineResult GetAssemblyOutline(int viewId, SolidContacts.OutlineOptions? options = null,
            IReadOnlyCollection<int>? modelIds = null) =>
            TeklaDrawingAssemblyOutlineApi.Build(viewId, modelIds!, this, options, restricted: true,
                visibleCount: solids.Count, requestedIds: modelIds!.ToArray());

        public PartSolidGeometryInViewResult GetPartSolidGeometryInView(int viewId, int modelId) => solids[modelId];
    }

    private static string? FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "cases", "dimension_context_cases");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
