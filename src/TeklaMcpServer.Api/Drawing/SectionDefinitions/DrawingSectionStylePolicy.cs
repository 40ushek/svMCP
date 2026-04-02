namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class DrawingSectionStylePolicy
{
    public double? ScaleDenominator { get; set; }
    public string CutViewAttributesFile { get; set; } = string.Empty;
    public string CutViewSymbolAttributesFile { get; set; } = string.Empty;
}
