namespace TeklaMcpServer.Api.Drawing.MarkDefinitions;

public sealed class TeklaMarkDefinitionApi : IMarkDefinitionApi
{
    public GetMarkDefinitionPresetResult GetDefaultPreset(DrawingMarkDefinitionScope scope)
    {
        return scope switch
        {
            DrawingMarkDefinitionScope.Assembly => new GetMarkDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateAssemblyPreset()
            },
            DrawingMarkDefinitionScope.Ga => new GetMarkDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateGaPreset(),
                Warnings =
                {
                    "GA mark preset currently reuses the assembly-oriented baseline until GA-specific defaults are defined."
                }
            },
            _ => new GetMarkDefinitionPresetResult
            {
                Success = false,
                Scope = scope,
                Error = $"Unsupported mark definition scope: {scope}."
            }
        };
    }

    private static DrawingMarkPreset CreateAssemblyPreset()
    {
        return new DrawingMarkPreset
        {
            Name = "assembly-standard",
            Description = "Legacy-inspired assembly mark preset with part marks enabled by default, outside-contour preference, leader-line fallback, and optional bolt marks.",
            DefinitionSet = new DrawingMarkDefinitionSet
            {
                Scope = DrawingMarkDefinitionScope.Assembly,
                Definitions =
                {
                    new DrawingMarkDefinition
                    {
                        ScenarioKind = DrawingMarkScenarioKind.PartMark,
                        TargetKind = DrawingMarkTargetKind.Part,
                        Placement = new DrawingMarkPlacementPolicy
                        {
                            PreferredMode = DrawingMarkPlacementMode.Auto,
                            PreferOutsideContour = true,
                            AllowLeaderLine = true,
                            AllowInsidePlacement = true
                        },
                        Content = new DrawingMarkContentPolicy
                        {
                            AttributeNames = { "PART_POS" }
                        },
                        Style = new DrawingMarkStylePolicy
                        {
                            AttributesFileName = "standard"
                        }
                    },
                    new DrawingMarkDefinition
                    {
                        ScenarioKind = DrawingMarkScenarioKind.BoltMark,
                        TargetKind = DrawingMarkTargetKind.Bolt,
                        IsEnabled = false,
                        Placement = new DrawingMarkPlacementPolicy
                        {
                            PreferredMode = DrawingMarkPlacementMode.LeaderLine,
                            AllowLeaderLine = true,
                            AllowInsidePlacement = false
                        },
                        Content = new DrawingMarkContentPolicy(),
                        Style = new DrawingMarkStylePolicy
                        {
                            AttributesFileName = "standard"
                        }
                    }
                }
            }
        };
    }

    private static DrawingMarkPreset CreateGaPreset()
    {
        var preset = CreateAssemblyPreset();
        preset.Name = "ga-standard";
        preset.Description = "Baseline GA mark preset reusing the current assembly-oriented defaults until GA-specific rules are introduced.";
        preset.DefinitionSet.Scope = DrawingMarkDefinitionScope.Ga;
        return preset;
    }
}
