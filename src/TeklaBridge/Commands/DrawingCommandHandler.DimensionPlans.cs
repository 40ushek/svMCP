using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler
{
    private bool HandleStructuralDimensionPlan(string[] args, bool apply)
    {
        if (args.Length < 2 || (apply && (args.Length < 3 || string.IsNullOrWhiteSpace(args[2]))))
        {
            WriteError("Requires plan JSON; apply also requires approvalToken returned by preview");
            return true;
        }
        try
        {
            var plan = JsonSerializer.Deserialize<StructuralDimensionPlan>(args[1],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("Plan is null");
            if (new DrawingHandler().GetActiveDrawing() is not AssemblyDrawing)
                throw new InvalidOperationException("An AssemblyDrawing must be active");
            var identity = ReadSourceIdentity(plan.ViewId);
            var exclusions = PartExclusions.Parse(plan.ExcludePrefixes, plan.ExcludeMaterials);
            var outline = GetStructuralOutline(plan.ViewId,
                included => CaptureParts(identity, "included", included.Select(p => p.ModelId)), exclusions);
            if (!identity.PartsCaptured || identity.FromActiveSheetOnly || identity.DrawingGuid.Length == 0 ||
                string.IsNullOrEmpty(identity.AssemblyGuid) || identity.UnresolvedPartIds.Count != 0)
                throw new InvalidOperationException("Source identity is incomplete");
            var group = StructuralGeometryGroupBuilder.Build(outline);
            CalcDimensionChains.Apply(group);
            var fingerprint = StructuralPlanFingerprint(plan.ViewId, identity, outline, group, exclusions);
            var prepared = StructuralDimensionPlanBuilder.Prepare(plan, group, fingerprint,
                outline.Included.Where(p => p.IsMainPart).Select(p => p.ModelId).ToArray(),
                outline.Included.Concat(outline.Excluded).All(p => p.IsMainPartKnown));
            if (apply && args[2] != prepared.ApprovalToken)
                throw new InvalidOperationException("Plan changed since preview; preview again before applying");
            // No drawing mutation above this point. Deliberately only Create in the first version.
            var write = apply
                ? new TeklaDrawingDimensionsApi().CreateDimension(plan.ViewId, prepared.Points,
                    prepared.Direction, plan.Distance, plan.AttributesFile, plan.RowType)
                : null;
            WriteJson(new
            {
                success = write == null || write.Created,
                previewOnly = !apply,
                scope = "one steel PartLocation chain; other sides are not reviewed",
                source = Describe(identity),
                sourceFingerprint = fingerprint,
                approvalToken = prepared.ApprovalToken,
                plan,
                points = prepared.Points,
                direction = prepared.Direction,
                policyStage = prepared.ReviewedChain.Stage.ToString(),
                dimensionId = write?.DimensionId,
                writeState = write?.WriteState,
                error = write?.Error,
                limitations = "Full-solid projection may extend beyond section depth. Preview checks geometry/decisions, not drafting sufficiency. Attributes load and row type are checked before apply. Relative rows only. No automatic retry, deduplication, replacement, or whole-view completion claim."
            });
        }
        catch (Exception ex) { WriteError(ex.Message); }
        return true;
    }

    private string StructuralPlanFingerprint(int viewId, SourceIdentity identity,
        StructuralOutline outline, GeometryGroup group, IReadOnlyList<PartExclusionRule> exclusions)
    {
        var view = FindView(new DrawingHandler().GetActiveDrawing(), viewId)
            ?? throw new ViewNotFoundException(viewId);
        if (!view.Select()) throw new InvalidOperationException("Cannot refresh view for plan fingerprint");
        var cs = view.ViewCoordinateSystem;
        var display = view.DisplayCoordinateSystem;
        var depth = ViewDepthWindow.Read(_model, view);
        var context = JsonSerializer.Serialize(new
        {
            viewId, source = Describe(identity),
            viewCoordinates = new[] { cs.Origin.X, cs.Origin.Y, cs.Origin.Z, cs.AxisX.X, cs.AxisX.Y, cs.AxisX.Z, cs.AxisY.X, cs.AxisY.Y, cs.AxisY.Z },
            displayCoordinates = new[] { display.Origin.X, display.Origin.Y, display.Origin.Z, display.AxisX.X, display.AxisX.Y, display.AxisX.Z, display.AxisY.X, display.AxisY.Y, display.AxisY.Z },
            scale = view.Attributes.Scale,
            depthWindow = depth.Box,
            depthError = depth.Error,
            exclusions = exclusions.Select(r => r.Id),
            parts = outline.Included.Concat(outline.Excluded).Select(p => new { p.ModelId, p.IsMainPart, p.IsMainPartKnown })
        });
        return StructuralDimensionPlanBuilder.Fingerprint(group.DimensionChains!, context);
    }
}
