using System.Collections.Generic;

namespace TeklaMcpServer.Api.Drawing;

public enum DrawingTextBoxSourceKind
{
    Unknown = 0,
    Dimension = 1,
    Mark = 2
}

public sealed class DrawingTextBox
{
    public DrawingTextBoxSourceKind SourceKind { get; set; }

    public int SourceObjectId { get; set; }

    public string SourceObjectKind { get; set; } = string.Empty;

    public int TextIndex { get; set; }

    public string Text { get; set; } = string.Empty;

    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public List<double[]> Polygon { get; set; } = [];
}
