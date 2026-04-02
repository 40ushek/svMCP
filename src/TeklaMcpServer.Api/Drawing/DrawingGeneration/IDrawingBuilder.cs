namespace TeklaMcpServer.Api.Drawing.DrawingGeneration;

public interface IDrawingBuilder
{
    DrawingGenerationResult Build(DrawingGenerationRequest request);
}
