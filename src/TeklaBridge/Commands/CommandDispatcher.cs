using System.IO;
using Tekla.Structures.Model;

namespace TeklaBridge.Commands;

internal sealed class CommandDispatcher
{
    private readonly ICommandHandler[] _handlers;
    private readonly TeklaMcpServer.Api.Drawing.ViewDimensionContextProvider _dimensionContexts;

    public CommandDispatcher(Model model, TextWriter output,
        TeklaMcpServer.Api.Drawing.ViewDimensionContextProvider dimensionContexts)
    {
        _dimensionContexts = dimensionContexts;
        _handlers = new ICommandHandler[]
        {
            new ModelCommandHandler(model, output),
            new DrawingCommandHandler(model, output, dimensionContexts)
        };
    }

    public bool Dispatch(string command, string[] args)
    {
        // These commands have a view ID in their first argument (not a dimension ID).
        // Merely reading dimensions on another view also ends the previous view run.
        if (command is "get_drawing_dimensions" or "create_dimension" or "get_structural_chain_positions"
            or "get_view_dimension_context" or "get_structural_outline" or "get_assembly_outline"
            or "get_drawing_view_context" or "get_all_parts_geometry_in_view" or "get_part_geometry_in_view"
            or "get_contact_candidate_points" or "draw_structural_chain_positions"
            or "get_dimension_contexts" or "capture_dimension_observation")
        {
            if (args.Length > 1 && int.TryParse(args[1], out var viewId))
                _dimensionContexts.ObserveView(viewId);
        }
        foreach (var handler in _handlers)
            if (handler.TryHandle(command, args))
                return true;
        return false;
    }
}
