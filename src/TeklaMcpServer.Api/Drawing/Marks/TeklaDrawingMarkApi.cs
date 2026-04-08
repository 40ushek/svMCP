using Tekla.Structures.Drawing;
using Tekla.Structures.Model;

namespace TeklaMcpServer.Api.Drawing;

public sealed partial class TeklaDrawingMarkApi : IDrawingMarkApi
{
    private readonly Model _model;

    public TeklaDrawingMarkApi(Model model) => _model = model;

    private static PropertyElement? CreatePropertyElement(string attributeName) =>
        (attributeName ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PART_POS" or "PARTPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition()),
            "PROFILE" or "PART_PROFILE"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile()),
            "MATERIAL" or "PART_MATERIAL"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material()),
            "ASSEMBLY_POS" or "ASSEMBLYPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition()),
            "NAME" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name()),
            "CLASS" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class()),
            "SIZE" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size()),
            "CAMBER" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber()),
            _ => null
        };

    private static PropertyElement? CreateSetMarkContentPropertyElement(string attributeName) =>
        (attributeName ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PART_POS" or "PARTPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.PartPosition()),
            "PROFILE" or "PART_PROFILE"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Profile()),
            "MATERIAL" or "PART_MATERIAL"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Material()),
            "ASSEMBLY_POS" or "PART_PREFIX" or "ASSEMBLYPOSITION"
                => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition()),
            "NAME" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Name()),
            "CLASS" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Class()),
            "SIZE" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Size()),
            "CAMBER" => new PropertyElement(PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.Camber()),
            _ => null
        };

    private static IEnumerable<View> EnumerateViews(Tekla.Structures.Drawing.Drawing drawing)
    {
        var enumerator = drawing.GetSheet().GetViews();
        while (enumerator.MoveNext())
            if (enumerator.Current is View v)
                yield return v;
    }
}
