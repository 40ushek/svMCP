using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TeklaMcpServer.Tools;

public static partial class ModelTools
{
    [McpServerTool, Description(
        "Read-only preview of one steel PartLocation chain using get_structural_chain_positions indices and sourceFingerprint. " +
        "Plan JSON: viewId, sourceFingerprint, side (Top/Bottom/Left/Right), referenceModelId (confirmed main part), subjectModelId, " +
        "ruleSet=steel, purpose=PartLocation, internalPolicy (None/Necessary/All), recognizableDistance (required for Necessary), " +
        "datumReason, closureReason, rowType=Relative, attributesFile, distance (non-negative), reverseStart, excludePrefixes, excludeMaterials, " +
        "positions:[{positionIndex,disposition:Kept/Removed,reason,supportIndex (Kept only)}]. Every position on this side needs a decision. " +
        "Returns resolved view-local points and approvalToken. Check preview before apply; it does not judge drafting sufficiency or other sides.")]
    public static string PreviewStructuralDimensionPlan(string planJson)
        => RunBridge("preview_structural_dimension_plan", planJson);

    [McpServerTool, Description(
        "Create the single chain exactly as previewed. Requires unchanged plan JSON and approvalToken from preview_structural_dimension_plan. " +
        "Re-reads source and validates before writing. Relative rows only. Preserves IDs/writeState on failure; inspect drawing before retry. " +
        "No replacement, deduplication, full-view completion or atomic rollback is implied.")]
    public static string ApplyStructuralDimensionPlan(string planJson, string approvalToken)
        => RunBridge("apply_structural_dimension_plan", planJson, approvalToken);
}
