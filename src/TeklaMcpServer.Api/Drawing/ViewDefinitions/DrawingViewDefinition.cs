namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class DrawingViewDefinition
{
    public DrawingViewFamilyKind FamilyKind { get; set; }
    public bool IsEnabled { get; set; }
    public double? ScaleDenominator { get; set; }
    public double? Shortening { get; set; }
    public string? AttributeProfileName { get; set; }
}
