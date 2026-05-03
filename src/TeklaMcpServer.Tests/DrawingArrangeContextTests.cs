using System.Collections.Generic;
using System.Runtime.Serialization;
using Tekla.Structures.Drawing;
using TeklaMcpServer.Api.Drawing;
using TeklaMcpServer.Api.Drawing.ViewLayout;
using Xunit;

namespace TeklaMcpServer.Tests;

public sealed class DrawingArrangeContextTests
{
    [Fact]
    public void WorkspaceConstructor_UsesWorkspaceSheetAndReservedFacts()
    {
        var drawingContext = new DrawingContext
        {
            Sheet = new DrawingSheetContext
            {
                Width = 420,
                Height = 297
            },
            ReservedLayout = new DrawingReservedLayoutContext
            {
                Margin = 12,
                Areas =
                [
                    new ReservedRect(300, 0, 420, 80)
                ]
            }
        };
        var workspace = DrawingLayoutWorkspace.From(drawingContext);
        var frameSizes = new Dictionary<int, (double Width, double Height)>
        {
            [10] = (60, 40)
        };

        var context = new DrawingArrangeContext(
            CreateDrawing(),
            workspace,
            views: [],
            gap: 8,
            effectiveFrameSizes: frameSizes);

        Assert.Same(workspace, context.Workspace);
        Assert.Equal(420, context.SheetWidth);
        Assert.Equal(297, context.SheetHeight);
        Assert.Equal(12, context.Margin);
        Assert.Equal(8, context.Gap);
        Assert.Same(workspace.ReservedAreas, context.ReservedAreas);
        Assert.Same(frameSizes, context.EffectiveFrameSizes);
    }

    private static Drawing CreateDrawing()
    {
#pragma warning disable SYSLIB0050
        return (Drawing)FormatterServices.GetUninitializedObject(typeof(AssemblyDrawing));
#pragma warning restore SYSLIB0050
    }
}
