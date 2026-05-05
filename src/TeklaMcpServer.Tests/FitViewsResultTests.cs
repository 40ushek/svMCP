using System.Text.Json;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class FitViewsResultTests
{
    [Fact]
    public void LayoutDiagnostics_IsInternalAndDoesNotChangeJsonContract()
    {
        var result = new FitViewsResult
        {
            OptimalScale = 20,
            LayoutDiagnostics = new DrawingCaseLayoutDiagnostics
            {
                SelectedCandidateName = "fit_views_to_sheet:planned-centered"
            }
        };

        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("layoutDiagnostics", json);
        Assert.DoesNotContain("LayoutDiagnostics", json);
    }
}
