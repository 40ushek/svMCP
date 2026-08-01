using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// Tekla-backed entry point for the candidate-point layer. It uses the strict topology
/// read, so a successful result never presents a partial solid as exact evidence.
/// </summary>
public sealed class TeklaDrawingPartCandidatePointApi : IDrawingPartCandidatePointApi
{
    private readonly IDrawingPartSolidGeometryApi _solidGeometryApi;

    public TeklaDrawingPartCandidatePointApi(Model model)
        : this(new TeklaDrawingPartSolidGeometryApi(model))
    {
    }

    internal TeklaDrawingPartCandidatePointApi(IDrawingPartSolidGeometryApi solidGeometryApi)
    {
        _solidGeometryApi = solidGeometryApi;
    }

    public GetPartCandidatePointsResult GetPartCandidatePointsInView(int viewId, int modelId)
    {
        var geometry = _solidGeometryApi.GetPartSolidGeometryInView(viewId, modelId);
        if (!geometry.Success)
        {
            return new GetPartCandidatePointsResult
            {
                Success = false,
                ViewId = viewId,
                ModelId = modelId,
                SolidGeometryComplete = false,
                Error = geometry.Error
            };
        }

        return new GetPartCandidatePointsResult
        {
            Success = true,
            ViewId = geometry.ViewId,
            ModelId = geometry.ModelId,
            SolidGeometryComplete = geometry.Solid.SolidGeometryComplete,
            Candidates = DrawingPartCandidatePointBuilder.Build(geometry)
        };
    }
}
