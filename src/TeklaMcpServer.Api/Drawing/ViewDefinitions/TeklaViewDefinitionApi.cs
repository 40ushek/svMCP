namespace TeklaMcpServer.Api.Drawing.ViewDefinitions;

public sealed class TeklaViewDefinitionApi : IViewDefinitionApi
{
    public GetViewDefinitionPresetResult GetDefaultPreset(DrawingViewDefinitionScope scope)
    {
        return scope switch
        {
            DrawingViewDefinitionScope.Assembly => new GetViewDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateAssemblyPreset()
            },
            DrawingViewDefinitionScope.Ga => new GetViewDefinitionPresetResult
            {
                Success = true,
                Scope = scope,
                Preset = CreateGaPreset(),
                Warnings =
                {
                    "GA preset currently reuses the assembly-oriented baseline until GA-specific defaults are defined."
                }
            },
            _ => new GetViewDefinitionPresetResult
            {
                Success = false,
                Scope = scope,
                Error = $"Unsupported view definition scope: {scope}."
            }
        };
    }

    private static DrawingViewPreset CreateAssemblyPreset()
    {
        return new DrawingViewPreset
        {
            Name = "assembly-standard",
            Description = "Legacy-oriented assembly view preset with along-axis creation, front-first view set, auto orientation and main-part visibility rules.",
            DefinitionSet = new DrawingViewDefinitionSet
            {
                Scope = DrawingViewDefinitionScope.Assembly,
                CreationMode = DrawingViewCreationMode.AlongAxis,
                Orientation = new DrawingViewOrientationPolicy
                {
                    CoordinateSystemSource = DrawingViewCoordinateSystemSource.Auto,
                    AxisRotationX = DrawingViewAxisRotation.Auto,
                    AxisRotationY = DrawingViewAxisRotation.Auto
                },
                Visibility = new DrawingViewVisibilityPolicy
                {
                    HideBackParts = false,
                    HideSideParts = true
                },
                Sheet = new DrawingViewSheetPolicy
                {
                    AutoSizeEnabled = false,
                    SizeMode = DrawingSheetSizeMode.Disabled
                },
                Views =
                {
                    new DrawingViewDefinition
                    {
                        FamilyKind = DrawingViewFamilyKind.Front,
                        IsEnabled = true,
                        ScaleDenominator = 10,
                        Shortening = 200,
                        AttributeProfileName = "standard"
                    },
                    new DrawingViewDefinition
                    {
                        FamilyKind = DrawingViewFamilyKind.Top,
                        IsEnabled = false,
                        ScaleDenominator = 10,
                        Shortening = 200,
                        AttributeProfileName = "standard"
                    },
                    new DrawingViewDefinition
                    {
                        FamilyKind = DrawingViewFamilyKind.Bottom,
                        IsEnabled = false,
                        ScaleDenominator = 10,
                        Shortening = 200,
                        AttributeProfileName = "standard"
                    },
                    new DrawingViewDefinition
                    {
                        FamilyKind = DrawingViewFamilyKind.ThreeDimensional,
                        IsEnabled = false,
                        ScaleDenominator = 15,
                        AttributeProfileName = "standard"
                    },
                    new DrawingViewDefinition
                    {
                        FamilyKind = DrawingViewFamilyKind.Section,
                        IsEnabled = false,
                        ScaleDenominator = 10,
                        AttributeProfileName = "standard"
                    }
                }
            }
        };
    }

    private static DrawingViewPreset CreateGaPreset()
    {
        var preset = CreateAssemblyPreset();
        preset.Name = "ga-standard";
        preset.Description = "Baseline GA preset reusing the current assembly-oriented default until GA-specific rules are introduced.";
        preset.DefinitionSet.Scope = DrawingViewDefinitionScope.Ga;
        return preset;
    }
}
