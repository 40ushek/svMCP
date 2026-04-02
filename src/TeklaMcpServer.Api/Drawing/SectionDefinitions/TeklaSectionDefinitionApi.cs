namespace TeklaMcpServer.Api.Drawing.SectionDefinitions;

public sealed class TeklaSectionDefinitionApi : ISectionDefinitionApi
{
    public GetSectionDefinitionPresetResult GetDefaultPreset(DrawingSectionDefinitionScope scope)
    {
        return scope switch
        {
            DrawingSectionDefinitionScope.Assembly => new GetSectionDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateAssemblyPreset()
            },
            DrawingSectionDefinitionScope.Ga => new GetSectionDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateGaPreset(),
                Warnings =
                {
                    "GA section preset currently reuses the assembly-oriented baseline until GA-specific defaults are defined."
                }
            },
            _ => new GetSectionDefinitionPresetResult
            {
                Success = false,
                Scope = scope,
                Error = $"Unsupported section definition scope: {scope}."
            }
        };
    }

    private static DrawingSectionPreset CreateAssemblyPreset()
    {
        return new DrawingSectionPreset
        {
            Name = "assembly-standard",
            Description = "Legacy-inspired section preset with along-axis sections as the main branch, section scale 1:10, standard cut-view attributes, configurable symbol direction, and merge policy for similar sections.",
            DefinitionSet = new DrawingSectionDefinitionSet
            {
                Scope = DrawingSectionDefinitionScope.Assembly,
                Definitions =
                {
                    new DrawingSectionDefinition
                    {
                        ScenarioKind = DrawingSectionScenarioKind.AlongAxis,
                        IsEnabled = false,
                        ExtendByAlongAxis = true,
                        SymbolDirection = DrawingSectionSymbolDirection.Auto,
                        Naming = new DrawingSectionNamingPolicy
                        {
                            UseIdenticalSectionSymbol = false
                        },
                        Merge = new DrawingSectionMergePolicy
                        {
                            MergeSimilarSections = false
                        },
                        Style = new DrawingSectionStylePolicy
                        {
                            ScaleDenominator = 10,
                            CutViewAttributesFile = "standard",
                            CutViewSymbolAttributesFile = "standard"
                        }
                    },
                    new DrawingSectionDefinition
                    {
                        ScenarioKind = DrawingSectionScenarioKind.AcrossAxis,
                        IsEnabled = false,
                        ExtendByAlongAxis = false,
                        SymbolDirection = DrawingSectionSymbolDirection.Auto,
                        Naming = new DrawingSectionNamingPolicy
                        {
                            UseIdenticalSectionSymbol = false
                        },
                        Merge = new DrawingSectionMergePolicy
                        {
                            MergeSimilarSections = false
                        },
                        Style = new DrawingSectionStylePolicy
                        {
                            ScaleDenominator = 10,
                            CutViewAttributesFile = "standard",
                            CutViewSymbolAttributesFile = "standard"
                        }
                    }
                }
            }
        };
    }

    private static DrawingSectionPreset CreateGaPreset()
    {
        var preset = CreateAssemblyPreset();
        preset.Name = "ga-standard";
        preset.Description = "Baseline GA section preset reusing the current assembly-oriented defaults until GA-specific rules are introduced.";
        preset.DefinitionSet.Scope = DrawingSectionDefinitionScope.Ga;
        return preset;
    }
}
