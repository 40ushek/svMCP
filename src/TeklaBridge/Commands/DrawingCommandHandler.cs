using System;
using Tekla.Structures.Model;

namespace TeklaBridge.Commands;

internal sealed partial class DrawingCommandHandler : ICommandHandler
{
    private const string NoActiveDrawingErrorJson = "{\"error\":\"No drawing is currently open\"}";
    private const string NoMatchingModelIdsInDrawingErrorJson = "{\"error\":\"None of the specified model IDs were found in the active drawing\"}";

    private readonly Model _model;
    private readonly TextWriter _output;

    public DrawingCommandHandler(Model model, TextWriter output)
    {
        _model = model;
        _output = output;
    }

    public bool TryHandle(string command, string[] args)
    {
        switch (command)
        {
            case "list_drawings":
            case "find_drawings":
            case "open_drawing":
            case "close_drawing":
            case "export_drawings_pdf":
            case "find_drawings_by_properties":
                return TryHandleDrawingCatalogCommands(command, args);

            case "create_ga_drawing":
            case "create_single_part_drawing":
            case "create_assembly_drawing":
                return TryHandleDrawingCreationCommands(command, args);

            case "select_drawing_objects":
            case "filter_drawing_objects":
            case "get_drawing_context":
            case "get_sheet_objects_debug":
                return TryHandleDrawingInteractionCommands(command, args);

            case "get_drawing_views":
            case "move_view":
            case "set_view_scale":
            case "fit_views_to_sheet":
            case "get_drawing_reserved_areas":
                return TryHandleViewCommands(command, args);

            case "get_drawing_dimensions":
            case "arrange_dimensions":
            case "move_dimension":
            case "create_dimension":
            case "delete_dimension":
            case "place_control_diagonals":
                return TryHandleDimensionCommands(command, args);

            case "get_part_geometry_in_view":
            case "get_all_parts_geometry_in_view":
            case "get_grid_axes":
            case "get_drawing_parts":
            case "draw_debug_overlay":
            case "draw_selected_mark_part_axis_geometry":
            case "clear_debug_overlay":
                return TryHandleGeometryCommands(command, args);

            case "arrange_marks":
            case "arrange_marks_no_collisions":
            case "create_part_marks":
            case "set_mark_content":
            case "delete_all_marks":
            case "resolve_mark_overlaps":
            case "get_drawing_marks":
                return TryHandleMarkCommands(command, args);

            default:
                return false;
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────
}
