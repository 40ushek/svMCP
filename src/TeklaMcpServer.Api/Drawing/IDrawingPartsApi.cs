namespace TeklaMcpServer.Api.Drawing;

public interface IDrawingPartsApi
{
    /// <summary>
    /// Returns all model objects referenced by the active drawing,
    /// using DrawingHandler.GetModelObjectIdentifiers — direct API lookup, no sheet enumeration.
    /// </summary>
    GetDrawingPartsResult GetDrawingParts();
}
