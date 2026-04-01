using TeklaMcpServer.Api.Drawing;
namespace TeklaMcpServer.Api.Drawing.ViewLayout;

public enum DrawingScalePolicy
{
    UniformAllNonDetail = 0,
    UniformMainWithSectionExceptions = 1,
    PreserveExistingScales = 2
}

