using System;
using TeklaMcpServer.Api.Drawing.ViewDefinitions;

namespace TeklaMcpServer.Api.Drawing.DrawingGeneration;

public sealed class DrawingBuilder : IDrawingBuilder
{
    private readonly IDrawingCreationApi _creationApi;
    private readonly IViewDefinitionApi _viewDefinitionApi;

    public DrawingBuilder(
        IDrawingCreationApi creationApi,
        IViewDefinitionApi viewDefinitionApi)
    {
        _creationApi = creationApi;
        _viewDefinitionApi = viewDefinitionApi;
    }

    public DrawingGenerationResult Build(DrawingGenerationRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var result = new DrawingGenerationResult
        {
            Kind = request.Kind
        };

        var validationError = Validate(request);
        if (validationError != null)
        {
            result.Error = validationError;
            return result;
        }

        ResolveDefaultViewPresetIfRequested(request, result);

        switch (request.Kind)
        {
            case DrawingGenerationKind.Assembly:
                result.Drawing = _creationApi.CreateAssemblyDrawing(
                    request.ModelObjectId!.Value,
                    request.DrawingProperties,
                    request.OpenDrawing);
                result.Success = result.Drawing.Created;
                break;

            case DrawingGenerationKind.SinglePart:
                result.Drawing = _creationApi.CreateSinglePartDrawing(
                    request.ModelObjectId!.Value,
                    request.DrawingProperties,
                    request.OpenDrawing);
                result.Success = result.Drawing.Created;
                break;

            case DrawingGenerationKind.Ga:
                result.GaDrawing = _creationApi.CreateGaDrawing(
                    request.ViewName!,
                    request.DrawingProperties,
                    request.OpenDrawing);
                result.Success = result.GaDrawing.Created;
                if (!result.Success && !string.IsNullOrWhiteSpace(result.GaDrawing.ErrorDetails))
                    result.Error = result.GaDrawing.ErrorDetails;
                break;

            default:
                result.Error = $"Unsupported generation kind: {request.Kind}.";
                break;
        }

        return result;
    }

    private void ResolveDefaultViewPresetIfRequested(
        DrawingGenerationRequest request,
        DrawingGenerationResult result)
    {
        if (!request.ResolveDefaultViewPreset)
            return;

        var scope = request.Kind switch
        {
            DrawingGenerationKind.Assembly => DrawingViewDefinitionScope.Assembly,
            DrawingGenerationKind.Ga => DrawingViewDefinitionScope.Ga,
            _ => (DrawingViewDefinitionScope?)null
        };

        if (scope == null)
            return;

        var presetResult = _viewDefinitionApi.GetDefaultPreset(scope.Value);
        if (!presetResult.Success)
        {
            var error = presetResult.Error;
            if (!string.IsNullOrWhiteSpace(error))
                result.Warnings.Add(error!);

            return;
        }

        result.ResolvedViewPreset = presetResult.Preset;
        result.Warnings.AddRange(presetResult.Warnings);
    }

    private static string? Validate(DrawingGenerationRequest request)
    {
        return request.Kind switch
        {
            DrawingGenerationKind.Assembly or DrawingGenerationKind.SinglePart
                when request.ModelObjectId is null
                => "ModelObjectId is required for assembly and single-part drawing generation.",

            DrawingGenerationKind.Ga
                when string.IsNullOrWhiteSpace(request.ViewName)
                => "ViewName is required for GA drawing generation.",

            _ => null
        };
    }
}
