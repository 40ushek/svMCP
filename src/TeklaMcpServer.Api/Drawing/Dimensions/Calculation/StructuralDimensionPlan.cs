using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>One explicitly scoped location chain; not a claim that the view is fully dimensioned.</summary>
public sealed class StructuralDimensionPlan
{
    public int ViewId { get; set; }
    public string SourceFingerprint { get; set; } = "";
    public string Side { get; set; } = "";
    public int ReferenceModelId { get; set; }
    public int SubjectModelId { get; set; }
    public string InternalPolicy { get; set; } = "";
    public double? RecognizableDistance { get; set; }
    public string RuleSet { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string DatumReason { get; set; } = "";
    public string ClosureReason { get; set; } = "";
    public string RowType { get; set; } = "";
    public string AttributesFile { get; set; } = "";
    public double Distance { get; set; }
    public bool ReverseStart { get; set; }
    public string ExcludePrefixes { get; set; } = "";
    public string ExcludeMaterials { get; set; } = "";
    public List<DimensionPositionDecision> Positions { get; set; } = new();
}

public sealed class DimensionPositionDecision
{
    public int PositionIndex { get; set; }
    public string Disposition { get; set; } = "";
    public string Reason { get; set; } = "";
    public int? SupportIndex { get; set; }
}

public sealed class PreparedDimensionPlan
{
    public string ApprovalToken { get; }
    public double[] Points { get; }
    public string Direction { get; }
    public DimensionChainSet ReviewedChain { get; }
    internal PreparedDimensionPlan(string token, double[] points, string direction, DimensionChainSet chain)
    { ApprovalToken = token; Points = points; Direction = direction; ReviewedChain = chain; }
}

public static class StructuralDimensionPlanBuilder
{
    public static string Fingerprint(DimensionChainSet chains, string sourceContext)
        => Hash(JsonSerializer.Serialize(new
        {
            sourceContext,
            chains = chains.Chains.Select(c => new
            {
                side = c.Side.ToString(),
                positions = c.Positions.Select(p => new
                {
                    p.Coordinate,
                    supports = p.Supports.Select(s => new
                    {
                        s.Source.Id, s.ModelId, s.Source.IsHole, kind = s.Kind.ToString(),
                        point = new[] { s.Point.X, s.Point.Y, s.Point.Z }
                    })
                })
            })
        }));

    public static PreparedDimensionPlan Prepare(StructuralDimensionPlan plan, GeometryGroup group,
        string currentFingerprint, IReadOnlyList<int> mainPartModelIds, bool mainPartsKnown)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (!group.Completeness.IsComplete || group.DimensionChains == null)
            throw new InvalidOperationException("Structural geometry is incomplete");
        if (string.IsNullOrWhiteSpace(plan.SourceFingerprint) || plan.SourceFingerprint != currentFingerprint)
            throw new InvalidOperationException("Source changed; read structural chain positions and plan again");
        if (!mainPartsKnown || mainPartModelIds.Count != 1 || mainPartModelIds[0] != plan.ReferenceModelId)
            throw new InvalidOperationException("Location requires one confirmed main part as reference");
        if (plan.ViewId <= 0 || plan.SubjectModelId <= 0 || plan.SubjectModelId == plan.ReferenceModelId)
            throw new ArgumentException("A location plan needs a view and a distinct subject part");
        if (plan.RuleSet != "steel" || plan.Purpose != "PartLocation")
            throw new ArgumentException("This first plan writer supports steel PartLocation only");
        if (plan.InternalPolicy != "None" && plan.InternalPolicy != "Necessary" && plan.InternalPolicy != "All")
            throw new ArgumentException("InternalPolicy must be None, Necessary or All");
        if (plan.InternalPolicy == "Necessary" && (!plan.RecognizableDistance.HasValue ||
            !DimensionWriteProtocol.Finite(plan.RecognizableDistance.Value) || plan.RecognizableDistance.Value < 0))
            throw new ArgumentException("Necessary requires a finite non-negative RecognizableDistance");
        if (string.IsNullOrWhiteSpace(plan.DatumReason) || string.IsNullOrWhiteSpace(plan.ClosureReason) ||
            string.IsNullOrWhiteSpace(plan.RowType) || string.IsNullOrWhiteSpace(plan.AttributesFile))
            throw new ArgumentException("State datum, closure, row type and attributes explicitly");
        // Running rows need a separate verified datum contract; never guess it from read order.
        if (!string.Equals(plan.RowType, "Relative", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Initial plan writer supports Relative rows only; running datum is not yet verified");
        if (!Enum.TryParse(plan.Side, true, out DimensionChainSide side) || !Enum.IsDefined(typeof(DimensionChainSide), side))
            throw new ArgumentException("Side must be Top, Bottom, Left or Right");
        var source = group.DimensionChains[side];
        if (plan.Positions == null || plan.Positions.Count != source.Positions.Count ||
            plan.Positions.Any(d => d == null) || plan.Positions.Select(d => d.PositionIndex).Distinct().Count() != source.Positions.Count ||
            plan.Positions.Any(d => d.PositionIndex < 0 || d.PositionIndex >= source.Positions.Count))
            throw new ArgumentException("Every position on this side needs exactly one decision");
        var selected = new List<(double X, double Y, int? Owner)>();
        var reviewed = new DimensionChain(source.Side, source.Direction, source.PlacementNormal);
        foreach (var decision in plan.Positions.OrderBy(d => d.PositionIndex))
        {
            if (string.IsNullOrWhiteSpace(decision.Reason)) throw new ArgumentException("Every decision needs a reason");
            var position = source.Positions[decision.PositionIndex];
            foreach (var support in position.Supports) reviewed.Add(position.Coordinate, support);
            if (decision.Disposition == "Removed")
            {
                if (decision.SupportIndex.HasValue) throw new ArgumentException("Removed position must not select a support");
                continue;
            }
            if (decision.Disposition != "Kept" || !decision.SupportIndex.HasValue ||
                decision.SupportIndex < 0 || decision.SupportIndex >= position.Supports.Count)
                throw new ArgumentException("Kept position requires a valid SupportIndex");
            var chosen = position.Supports[decision.SupportIndex.Value];
            if (chosen.ModelId != plan.ReferenceModelId && chosen.ModelId != plan.SubjectModelId)
                throw new ArgumentException("Location supports must belong to the reference or subject, not group bbox or another part");
            selected.Add((chosen.Point.X, chosen.Point.Y, chosen.ModelId));
        }
        if (!selected.Any(p => p.Owner == plan.ReferenceModelId) || !selected.Any(p => p.Owner == plan.SubjectModelId))
            throw new ArgumentException("A location chain must contain both reference and subject supports; size alone is not location");
        if (plan.InternalPolicy == "None" && selected.Count(p => p.Owner == plan.SubjectModelId) > 1)
            throw new ArgumentException("Internal=None supports one subject location, not multiple internal positions");
        var horizontal = side == DimensionChainSide.Top || side == DimensionChainSide.Bottom;
        for (var i = 1; i < selected.Count; i++)
            if ((horizontal ? selected[i].X - selected[i - 1].X : selected[i].Y - selected[i - 1].Y) <= 0.01)
                throw new ArgumentException("Selected supports must have distinct, increasing coordinates along the chain");
        if (plan.ReverseStart) selected.Reverse();
        var points = selected.SelectMany(p => new[] { p.X, p.Y, 0d }).ToArray();
        var error = DimensionWriteProtocol.Validate(points, plan.Distance);
        if (error != null) throw new ArgumentException(error);
        // Clone policy state; rejecting a plan must not mutate the source snapshot.
        reviewed.FinalizePositions(0);
        for (var i = 0; i < reviewed.Positions.Count; i++)
        {
            var d = plan.Positions.Single(item => item.PositionIndex == i);
            if (d.Disposition == "Kept") reviewed.Positions[i].MarkKept(d.Reason);
            else reviewed.Positions[i].MarkRemoved(d.Reason);
        }
        var chains = new DimensionChainSet(new[] { reviewed });
        chains.MarkPolicyApplied();
        var direction = side switch
        {
            DimensionChainSide.Top => "horizontal", DimensionChainSide.Bottom => "horizontal-down",
            DimensionChainSide.Left => "vertical-left", _ => "vertical"
        };
        return new PreparedDimensionPlan(Hash(currentFingerprint + JsonSerializer.Serialize(plan)), points, direction, chains);
    }

    private static string Hash(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }
}
