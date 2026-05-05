using System;
using System.IO;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

internal sealed class DrawingCaseSnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DrawingCaseSnapshotSaveResult Save(
        string rootDirectory,
        string drawingCategory,
        string operation,
        DrawingContext before,
        DrawingContext after,
        string? note = null,
        DrawingLayoutScore? scoreBefore = null,
        DrawingLayoutScore? scoreAfter = null,
        DrawingCaseLayoutDiagnostics? layoutDiagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Root directory is required.", nameof(rootDirectory));

        if (string.IsNullOrWhiteSpace(drawingCategory))
            throw new ArgumentException("Drawing category is required.", nameof(drawingCategory));

        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Operation is required.", nameof(operation));

        var drawingGuid = ResolveDrawingGuid(before, after);
        var caseDirectory = Path.Combine(rootDirectory, drawingCategory.Trim(), drawingGuid);
        Directory.CreateDirectory(caseDirectory);

        var beforePath = Path.Combine(caseDirectory, "before.json");
        var afterPath = Path.Combine(caseDirectory, "after.json");
        var metaPath = Path.Combine(caseDirectory, "meta.json");

        File.WriteAllText(beforePath, JsonSerializer.Serialize(before, JsonOptions));
        File.WriteAllText(afterPath, JsonSerializer.Serialize(after, JsonOptions));
        File.WriteAllText(metaPath, JsonSerializer.Serialize(
            CreateMeta(
                drawingGuid,
                operation,
                before,
                after,
                note,
                scoreBefore,
                scoreAfter,
                layoutDiagnostics),
            JsonOptions));

        return new DrawingCaseSnapshotSaveResult
        {
            CaseDirectory = caseDirectory,
            BeforePath = beforePath,
            AfterPath = afterPath,
            MetaPath = metaPath
        };
    }

    private static DrawingCaseMeta CreateMeta(
        string drawingGuid,
        string operation,
        DrawingContext before,
        DrawingContext after,
        string? note,
        DrawingLayoutScore? scoreBefore,
        DrawingLayoutScore? scoreAfter,
        DrawingCaseLayoutDiagnostics? layoutDiagnostics)
    {
        var drawing = after.Drawing ?? before.Drawing ?? new DrawingInfo();

        return new DrawingCaseMeta
        {
            DrawingGuid = drawingGuid,
            DrawingType = drawing.Type ?? string.Empty,
            DrawingName = drawing.Name ?? string.Empty,
            Operation = operation,
            Note = note ?? string.Empty,
            ScoreBefore = scoreBefore == null ? null : CreateScoreSummary(scoreBefore),
            ScoreAfter = scoreAfter == null ? null : CreateScoreSummary(scoreAfter),
            LayoutDiagnostics = layoutDiagnostics
        };
    }

    private static DrawingCaseScoreSummary CreateScoreSummary(DrawingLayoutScore score)
    {
        return new DrawingCaseScoreSummary
        {
            Total = score.TotalScore,
            Breakdown = new DrawingCaseScoreBreakdown
            {
                FillRatio = score.Breakdown.FillRatioScore,
                UniformScaleScore = score.Breakdown.UniformScaleScore,
                ViewOverlapPenalty = score.Breakdown.ViewOverlapPenalty,
                ReservedAreaOverlapPenalty = score.Breakdown.ReservedOverlapPenalty
            }
        };
    }

    private static string ResolveDrawingGuid(DrawingContext before, DrawingContext after)
    {
        var beforeGuid = before.Drawing?.Guid?.Trim();
        var afterGuid = after.Drawing?.Guid?.Trim();

        if (string.IsNullOrWhiteSpace(beforeGuid) && string.IsNullOrWhiteSpace(afterGuid))
            throw new InvalidOperationException("Drawing snapshot requires drawing_guid in before or after context.");

        if (!string.IsNullOrWhiteSpace(beforeGuid) &&
            !string.IsNullOrWhiteSpace(afterGuid) &&
            !string.Equals(beforeGuid, afterGuid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Before and after contexts reference different drawing_guid values.");
        }

        // Prefer the post-operation snapshot as the canonical case identity,
        // but keep the pre-operation guid as a fallback when after is incomplete.
        return afterGuid ?? beforeGuid ?? throw new InvalidOperationException("Drawing snapshot requires drawing_guid in before or after context.");
    }
}

internal sealed class DrawingCaseSnapshotSaveResult
{
    public string CaseDirectory { get; set; } = string.Empty;
    public string BeforePath { get; set; } = string.Empty;
    public string AfterPath { get; set; } = string.Empty;
    public string MetaPath { get; set; } = string.Empty;
}

internal sealed class DrawingCaseMeta
{
    public string DrawingGuid { get; set; } = string.Empty;
    public string DrawingType { get; set; } = string.Empty;
    public string DrawingName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DrawingCaseScoreSummary? ScoreBefore { get; set; }
    public DrawingCaseScoreSummary? ScoreAfter { get; set; }
    public DrawingCaseLayoutDiagnostics? LayoutDiagnostics { get; set; }
}

internal sealed class DrawingCaseLayoutDiagnostics
{
    public string SelectedCandidateName { get; set; } = string.Empty;
    public double? SelectedCandidateScore { get; set; }
    public bool? SelectedCandidateFeasible { get; set; }
    public DrawingCaseApplyPlanSummary? ApplyPlan { get; set; }
    public DrawingCaseApplyDeltaSummary? ApplyDelta { get; set; }
    public DrawingCaseApplySafetySummary? ApplySafety { get; set; }
    public List<string> Diagnostics { get; set; } = new();
}

internal sealed class DrawingCaseApplyPlanSummary
{
    public bool CanApply { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int MoveCount { get; set; }
}

internal sealed class DrawingCaseApplyDeltaSummary
{
    public int MoveCount { get; set; }
    public int ComparableMoveCount { get; set; }
    public int MissingBaselineCount { get; set; }
    public int MovedCount { get; set; }
    public int ScaleChangedCount { get; set; }
    public double MaxDelta { get; set; }
    public double AverageDelta { get; set; }
}

internal sealed class DrawingCaseApplySafetySummary
{
    public string RequestedMode { get; set; } = string.Empty;
    public string EffectiveMode { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class DrawingCaseScoreSummary
{
    public double Total { get; set; }
    public DrawingCaseScoreBreakdown Breakdown { get; set; } = new();
}

internal sealed class DrawingCaseScoreBreakdown
{
    public double FillRatio { get; set; }
    public double UniformScaleScore { get; set; }
    public double ViewOverlapPenalty { get; set; }
    public double ReservedAreaOverlapPenalty { get; set; }
}
