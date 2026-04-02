namespace TeklaMcpServer.Api.Drawing.DimensionDefinitions;

public sealed class TeklaDimensionDefinitionApi : IDimensionDefinitionApi
{
    public GetDimensionDefinitionPresetResult GetDefaultPreset(DrawingDimensionDefinitionScope scope)
    {
        return scope switch
        {
            DrawingDimensionDefinitionScope.Assembly => new GetDimensionDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateAssemblyPreset()
            },
            DrawingDimensionDefinitionScope.Ga => new GetDimensionDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateGaPreset(),
                Warnings =
                {
                    "GA dimension preset currently reuses the assembly-oriented baseline until GA-specific defaults are defined."
                }
            },
            _ => new GetDimensionDefinitionPresetResult
            {
                Success = false,
                Scope = scope,
                Error = $"Unsupported dimension definition scope: {scope}."
            }
        };
    }

    private static DrawingDimensionPreset CreateAssemblyPreset()
    {
        return new DrawingDimensionPreset
        {
            Name = "assembly-standard",
            Description = "Legacy-inspired assembly dimension preset with source-driven overall and assembly dimensions, optional node and bolt branches, and deferred control diagonals.",
            DefinitionSet = new DrawingDimensionDefinitionSet
            {
                Scope = DrawingDimensionDefinitionScope.Assembly,
                Definitions =
                {
                    new DrawingDimensionDefinition
                    {
                        ScenarioKind = DrawingDimensionScenarioKind.Overall,
                        Sources = { DrawingDimensionSourceKind.Axis, DrawingDimensionSourceKind.Assembly },
                        Placement = new DrawingDimensionPlacementPolicy
                        {
                            DefaultDistance = 10.0,
                            DirectionHint = "along-axis",
                            AttributesFileName = "standard"
                        },
                        Points = new DrawingDimensionPointPolicy
                        {
                            UseCharacteristicPoints = true,
                            UseExtremePoints = true
                        }
                    },
                    new DrawingDimensionDefinition
                    {
                        ScenarioKind = DrawingDimensionScenarioKind.Assembly,
                        Sources = { DrawingDimensionSourceKind.Assembly, DrawingDimensionSourceKind.Part },
                        Placement = new DrawingDimensionPlacementPolicy
                        {
                            DefaultDistance = 10.0,
                            AttributesFileName = "standard"
                        },
                        Points = new DrawingDimensionPointPolicy
                        {
                            UseCharacteristicPoints = true
                        }
                    },
                    new DrawingDimensionDefinition
                    {
                        ScenarioKind = DrawingDimensionScenarioKind.Node,
                        IsEnabled = false,
                        Sources = { DrawingDimensionSourceKind.Node },
                        Placement = new DrawingDimensionPlacementPolicy
                        {
                            DefaultDistance = 10.0,
                            AttributesFileName = "standard"
                        },
                        Points = new DrawingDimensionPointPolicy
                        {
                            UseWorkPoints = true
                        }
                    },
                    new DrawingDimensionDefinition
                    {
                        ScenarioKind = DrawingDimensionScenarioKind.Bolt,
                        IsEnabled = false,
                        Sources = { DrawingDimensionSourceKind.Bolt, DrawingDimensionSourceKind.Part },
                        Placement = new DrawingDimensionPlacementPolicy
                        {
                            DefaultDistance = 10.0,
                            AttributesFileName = "standard"
                        },
                        Points = new DrawingDimensionPointPolicy
                        {
                            UseBoltPoints = true
                        }
                    },
                    new DrawingDimensionDefinition
                    {
                        ScenarioKind = DrawingDimensionScenarioKind.ControlDiagonal,
                        IsEnabled = false,
                        Sources = { DrawingDimensionSourceKind.Assembly },
                        Placement = new DrawingDimensionPlacementPolicy
                        {
                            DefaultDistance = 20.0,
                            AttributesFileName = "standard"
                        },
                        Points = new DrawingDimensionPointPolicy
                        {
                            UseExtremePoints = true
                        }
                    }
                }
            }
        };
    }

    private static DrawingDimensionPreset CreateGaPreset()
    {
        var preset = CreateAssemblyPreset();
        preset.Name = "ga-standard";
        preset.Description = "Baseline GA dimension preset reusing the current assembly-oriented defaults until GA-specific rules are introduced.";
        preset.DefinitionSet.Scope = DrawingDimensionDefinitionScope.Ga;
        return preset;
    }
}
