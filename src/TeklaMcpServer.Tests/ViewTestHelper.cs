using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TeklaMcpServer.Tests;

internal static class ViewTestHelper
{
    internal static View Create(
        View.ViewTypes viewType,
        string name = "",
        double width = 0,
        double height = 0,
        double originX = 0,
        double originY = 0,
        double scale = 0)
    {
#pragma warning disable SYSLIB0050
        var view = (View)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(View));
#pragma warning restore SYSLIB0050

        TrySetViewType(view, viewType);

        if (!string.IsNullOrEmpty(name))
            view.Name = name;

        if (width != 0 || height != 0)
        {
            view.Width = width;
            view.Height = height;
        }

        if (originX != 0 || originY != 0)
            view.Origin = new Point(originX, originY, 0);

        if (scale != 0)
            TrySetAttributesScale(view, scale);

        return view;
    }

    private static void TrySetAttributesScale(View view, double scale)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        // Use GetProperties to avoid AmbiguousMatchException when multiple overloads exist
        var attrProp = System.Array.Find(typeof(View).GetProperties(flags), p => p.Name == "Attributes");
        if (attrProp == null) return;

        var attr = attrProp.GetValue(view);
        if (attr == null)
        {
            var attrType = attrProp.PropertyType;
#pragma warning disable SYSLIB0050
            attr = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(attrType);
#pragma warning restore SYSLIB0050
            var setter = attrProp.GetSetMethod(nonPublic: true);
            if (setter != null)
                setter.Invoke(view, [attr]);
            else
            {
                foreach (var fname in new[] { "<Attributes>k__BackingField", "m_Attributes", "m_attributes", "_attributes" })
                {
                    var bf = typeof(View).GetField(fname, flags);
                    if (bf != null) { bf.SetValue(view, attr); break; }
                }
            }
            attr = attrProp.GetValue(view);
        }

        if (attr == null) return;

        var scaleProp = attr.GetType().GetProperty("Scale", flags);
        var scaleSetter = scaleProp?.GetSetMethod(nonPublic: true);
        if (scaleSetter != null)
            scaleSetter.Invoke(attr, [scale]);
    }

    private static void TrySetViewType(View view, View.ViewTypes viewType)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // Try internal/private setter on property
        var prop = typeof(View).GetProperty(nameof(View.ViewType), flags);
        if (prop?.GetSetMethod(nonPublic: true) is { } setter)
        {
            setter.Invoke(view, [viewType]);
            return;
        }

        // Common backing field name patterns
        foreach (var fieldName in new[] { "m_ViewType", "m_viewType", "_viewType", "<ViewType>k__BackingField" })
        {
            var named = typeof(View).GetField(fieldName, flags);
            if (named != null) { named.SetValue(view, viewType); return; }
        }

        // Last resort: any field with the exact enum type
        foreach (var field in typeof(View).GetFields(flags))
        {
            if (field.FieldType == typeof(View.ViewTypes))
            {
                field.SetValue(view, viewType);
                return;
            }
        }
    }
}
