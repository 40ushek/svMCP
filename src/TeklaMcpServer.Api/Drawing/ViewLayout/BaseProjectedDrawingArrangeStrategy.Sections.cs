using System.Collections.Generic;
using System.Linq;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.DrawingInternal;
using TeklaMcpServer.Api.Diagnostics;
using TeklaMcpServer.Api.Drawing;

namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public sealed partial class BaseProjectedDrawingArrangeStrategy
{
    private readonly struct SectionStackFailureInfo
    {
        public SectionStackFailureInfo(
            View section,
            ReservedRect rect,
            string rejectReason,
            string conflictReason)
        {
            Section = section;
            Rect = rect;
            RejectReason = rejectReason;
            ConflictReason = conflictReason;
        }

        public View Section { get; }
        public ReservedRect Rect { get; }
        public string RejectReason { get; }
        public string ConflictReason { get; }
    }

    internal readonly struct HorizontalSectionProbeResult
    {
        public HorizontalSectionProbeResult(
            bool success,
            ReservedRect rect,
            string rejectReason,
            string conflictReason,
            string diagnosticType,
            string diagnosticTarget)
        {
            Success = success;
            Rect = rect;
            RejectReason = rejectReason;
            ConflictReason = conflictReason;
            DiagnosticType = diagnosticType;
            DiagnosticTarget = diagnosticTarget;
        }

        public bool Success { get; }
        public ReservedRect Rect { get; }
        public string RejectReason { get; }
        public string ConflictReason { get; }
        public string DiagnosticType { get; }
        public string DiagnosticTarget { get; }
    }

    internal readonly struct VerticalSectionProbeResult
    {
        public VerticalSectionProbeResult(
            bool success,
            ReservedRect rect,
            string rejectReason,
            string conflictReason,
            string diagnosticType,
            string diagnosticTarget)
        {
            Success = success;
            Rect = rect;
            RejectReason = rejectReason;
            ConflictReason = conflictReason;
            DiagnosticType = diagnosticType;
            DiagnosticTarget = diagnosticTarget;
        }

        public bool Success { get; }
        public ReservedRect Rect { get; }
        public string RejectReason { get; }
        public string ConflictReason { get; }
        public string DiagnosticType { get; }
        public string DiagnosticTarget { get; }
    }

    internal readonly struct StandardSectionPartition
    {
        public StandardSectionPartition(IReadOnlyList<View> normal, IReadOnlyList<View> oversized)
        {
            Normal = normal;
            Oversized = oversized;
        }

        public IReadOnlyList<View> Normal { get; }
        public IReadOnlyList<View> Oversized { get; }
    }

    private static StandardSectionPartition PartitionStandardSections(
        DrawingArrangeContext context,
        ReservedRect baseRect,
        IReadOnlyList<View> sections,
        SectionPlacementSide placementSide)
    {
        if (sections.Count == 0)
            return new StandardSectionPartition(System.Array.Empty<View>(), System.Array.Empty<View>());

        var normal = new List<View>(sections.Count);
        var oversized = new List<View>();
        var baseWidth = baseRect.MaxX - baseRect.MinX;
        var baseHeight = baseRect.MaxY - baseRect.MinY;

        foreach (var section in sections)
        {
            var width = DrawingArrangeContextSizing.GetWidth(context, section);
            var height = DrawingArrangeContextSizing.GetHeight(context, section);
            if (IsOversizedStandardSection(placementSide, baseWidth, baseHeight, width, height, context.Gap))
                oversized.Add(section);
            else
                normal.Add(section);
        }

        return new StandardSectionPartition(normal, oversized);
    }

    private static string FormatSectionIds(IReadOnlyList<View> sectionViews)
        => string.Join(",", sectionViews.Select(v => v.GetIdentifier().ID));

    private static bool TryValidateSectionCandidateRect(
        ReservedRect rect,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        IEnumerable<ReservedRect> proposed,
        out string reason)
    {
        if (!IsWithinArea(rect, searchArea.FreeMinX, searchArea.FreeMaxX, searchArea.FreeMinY, searchArea.FreeMaxY))
        {
            reason = "out-of-bounds";
            return false;
        }

        if (IntersectsAny(rect, occupied))
        {
            reason = "occupied-intersection";
            return false;
        }

        if (proposed.Any(item => Intersects(item, rect)))
        {
            reason = "proposed-intersection";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryValidateSectionCandidateRect(
        ReservedRect rect,
        double freeMinX,
        double freeMaxX,
        double freeMinY,
        double freeMaxY,
        IReadOnlyList<ReservedRect> occupied,
        IEnumerable<ReservedRect> proposed,
        out string reason)
        => TryValidateSectionCandidateRect(
            rect,
            CreateSearchArea(freeMinX, freeMaxX, freeMinY, freeMaxY),
            occupied,
            proposed,
            out reason);

    private static void TraceSectionStackAttempt(string axis, RelativePlacement preferredZone, IReadOnlyList<View> sectionViews)
    {
        PerfTrace.Write(
            "api-view",
            "section_stack_attempt",
            0,
            $"axis={axis} preferred={preferredZone} sections=[{FormatSectionIds(sectionViews)}]");
    }

    private static void TraceSectionStackResult(
        string axis,
        RelativePlacement preferredZone,
        RelativePlacement? actualZone,
        bool fallbackUsed,
        IReadOnlyList<View> sectionViews)
    {
        PerfTrace.Write(
            "api-view",
            "section_stack_result",
            0,
            $"axis={axis} preferred={preferredZone} actual={(actualZone?.ToString() ?? "none")} fallbackUsed={(fallbackUsed ? 1 : 0)} sections=[{FormatSectionIds(sectionViews)}]");
    }

    private static void TraceVerticalSectionStackFailure(
        DrawingArrangeContext context,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        SectionStackFailureInfo? failure)
    {
        if (failure == null)
            return;

        if (failure.Value.RejectReason == "out-of-bounds-y")
        {
            var height = DrawingArrangeContextSizing.GetHeight(context, failure.Value.Section);
            PerfTrace.Write(
                "api-view",
                "section_stack_reject",
                0,
                $"axis=vertical zone={zone} section={failure.Value.Section.GetIdentifier().ID} reason=out-of-bounds-y height={height:F2} freeY={searchArea.FreeMinY:F2}..{searchArea.FreeMaxY:F2}");
            return;
        }

        PerfTrace.Write(
            "api-view",
            "section_stack_reject",
            0,
            $"axis=vertical zone={zone} section={failure.Value.Section.GetIdentifier().ID} reason={failure.Value.RejectReason} rect=({failure.Value.Rect.MinX:F2},{failure.Value.Rect.MinY:F2},{failure.Value.Rect.MaxX:F2},{failure.Value.Rect.MaxY:F2}) free=({searchArea.FreeMinX:F2},{searchArea.FreeMinY:F2},{searchArea.FreeMaxX:F2},{searchArea.FreeMaxY:F2})");
    }

    private static void TraceHorizontalSectionStackFailure(
        DrawingArrangeContext context,
        RelativePlacement zone,
        ViewPlacementSearchArea searchArea,
        IReadOnlyList<ReservedRect> occupied,
        IReadOnlyList<PlannedPlacement> planned,
        SectionStackFailureInfo? failure)
    {
        if (failure == null)
            return;

        var section = failure.Value.Section;
        var width = DrawingArrangeContextSizing.GetWidth(context, section);
        var height = DrawingArrangeContextSizing.GetHeight(context, section);

        if (failure.Value.RejectReason == "out-of-bounds-x")
        {
            PerfTrace.Write(
                "api-view",
                "section_stack_reject",
                0,
                $"axis=horizontal zone={zone} section={section.GetIdentifier().ID} reason=out-of-bounds-x size={width:F2}x{height:F2} freeX={searchArea.FreeMinX:F2}..{searchArea.FreeMaxX:F2}");
            return;
        }

        var preferredRect = failure.Value.Rect;
        var hasActualRect = DrawingViewFrameGeometry.TryGetBoundingRect(section, out var actualRect);
        var blockers = occupied
            .Where(blocked =>
                blocked.MinY < preferredRect.MaxY &&
                blocked.MaxY > preferredRect.MinY &&
                blocked.MaxX > preferredRect.MinX &&
                blocked.MinX < preferredRect.MaxX)
            .Select(blocked =>
                $"[{blocked.MinX:F2},{blocked.MinY:F2},{blocked.MaxX:F2},{blocked.MaxY:F2}]")
            .ToList();
        PerfTrace.Write(
            "api-view",
            "section_stack_reject",
            0,
            $"axis=horizontal zone={zone} section={section.GetIdentifier().ID} reason={failure.Value.RejectReason} y={preferredRect.MinY:F2}..{preferredRect.MaxY:F2} preferredRect=[{preferredRect.MinX:F2},{preferredRect.MinY:F2},{preferredRect.MaxX:F2},{preferredRect.MaxY:F2}] actualRect={(hasActualRect ? $"[{actualRect.MinX:F2},{actualRect.MinY:F2},{actualRect.MaxX:F2},{actualRect.MaxY:F2}]" : "n/a")} size={width:F2}x{height:F2} free=({searchArea.FreeMinX:F2},{searchArea.FreeMinY:F2},{searchArea.FreeMaxX:F2},{searchArea.FreeMaxY:F2}) occupied={occupied.Count} blockers={string.Join(";", blockers)}");
        PerfTrace.Write(
            "api-view",
            "section_stack_snapshot",
            0,
            $"axis=horizontal zone={zone} section={section.GetIdentifier().ID} candidate=[{preferredRect.MinX:F2},{preferredRect.MinY:F2},{preferredRect.MaxX:F2},{preferredRect.MaxY:F2}] planned=[{FormatPlannedRects(context, planned)}]");

        if (ShouldDebugStopOnSectionReject())
        {
            throw new System.InvalidOperationException(
                $"Debug stop: horizontal section reject section={section.GetIdentifier().ID} zone={zone} preferredRect=[{preferredRect.MinX:F2},{preferredRect.MinY:F2},{preferredRect.MaxX:F2},{preferredRect.MaxY:F2}] actualRect={(hasActualRect ? $"[{actualRect.MinX:F2},{actualRect.MinY:F2},{actualRect.MaxX:F2},{actualRect.MaxY:F2}]" : "n/a")}");
        }
    }

    private static bool TryProbeSectionStackWithFallback(
        RelativePlacement preferredZone,
        System.Func<RelativePlacement, bool> tryPlace,
        out RelativePlacement? actualZone,
        out bool fallbackUsed)
    {
        if (tryPlace(preferredZone))
        {
            actualZone = preferredZone;
            fallbackUsed = false;
            return true;
        }

        var fallbackZone = GetFallbackZone(preferredZone);
        if (tryPlace(fallbackZone))
        {
            actualZone = fallbackZone;
            fallbackUsed = true;
            return true;
        }

        actualZone = null;
        fallbackUsed = false;
        return false;
    }

    private static bool ShouldDebugStopOnSectionReject()
    {
        var raw = System.Environment.GetEnvironmentVariable("SVMCP_FIT_DEBUG_STOP_ON_SECTION_REJECT");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Equals("1", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHorizontalSectionPlacementInputs(
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double width,
        double height,
        double gap,
        ViewPlacementSearchArea searchArea,
        out double preferredMinX,
        out double minY,
        out double maxY)
    {
        var freeMinX = searchArea.FreeMinX;
        var freeMaxX = searchArea.FreeMaxX;
        preferredMinX = CenterX(frontRect) - width / 2.0;
        if (preferredMinX < freeMinX || preferredMinX + width > freeMaxX)
        {
            minY = 0;
            maxY = 0;
            return false;
        }

        if (zone == RelativePlacement.Top)
        {
            minY = anchorRect.MaxY + gap;
            maxY = minY + height;
            return true;
        }

        if (zone == RelativePlacement.Bottom)
        {
            maxY = anchorRect.MinY - gap;
            minY = maxY - height;
            return true;
        }

        minY = 0;
        maxY = 0;
        return false;
    }

    private static bool TryGetHorizontalSectionPlacementInputs(
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double width,
        double height,
        double gap,
        double freeMinX,
        double freeMaxX,
        out double preferredMinX,
        out double minY,
        out double maxY)
        => TryGetHorizontalSectionPlacementInputs(
            frontRect,
            anchorRect,
            zone,
            width,
            height,
            gap,
            CreateSearchArea(freeMinX, freeMaxX, 0, 0),
            out preferredMinX,
            out minY,
            out maxY);

    private static bool TryCreateVerticalSectionRect(
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double width,
        double height,
        double gap,
        ViewPlacementSearchArea searchArea,
        out ReservedRect rect)
    {
        var freeMinY = searchArea.FreeMinY;
        var freeMaxY = searchArea.FreeMaxY;
        var minY = CenterY(frontRect) - height / 2.0;
        if (minY < freeMinY || minY + height > freeMaxY)
        {
            rect = new ReservedRect(0, 0, 0, 0);
            return false;
        }

        if (zone == RelativePlacement.Right)
        {
            var minX = anchorRect.MaxX + gap;
            rect = new ReservedRect(minX, minY, minX + width, minY + height);
            return true;
        }

        if (zone == RelativePlacement.Left)
        {
            var maxX = anchorRect.MinX - gap;
            rect = new ReservedRect(maxX - width, minY, maxX, minY + height);
            return true;
        }

        rect = new ReservedRect(0, 0, 0, 0);
        return false;
    }

    private static bool TryCreateVerticalSectionRect(
        ReservedRect frontRect,
        ReservedRect anchorRect,
        RelativePlacement zone,
        double width,
        double height,
        double gap,
        double freeMinY,
        double freeMaxY,
        out ReservedRect rect)
        => TryCreateVerticalSectionRect(
            frontRect,
            anchorRect,
            zone,
            width,
            height,
            gap,
            CreateSearchArea(0, 0, freeMinY, freeMaxY),
            out rect);
}

