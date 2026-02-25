using System.Collections;
using System.IO;
using System.Text.Json;
using Tekla.Structures.Model;

namespace TeklaBridge;

internal partial class Program
{
    private static bool TryHandleModelCommand(string command, string[] args, Model model, TextWriter realOut)
    {
        switch (command)
        {
            case "get_selected_properties":
            {
                var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
                var objs = selector.GetSelectedObjects();
                var results = new System.Collections.Generic.List<object>();
                while (objs.MoveNext())
                {
                    if (objs.Current is Tekla.Structures.Model.Part part)
                    {
                        double weight = 0;
                        part.GetReportProperty("WEIGHT", ref weight);
                        results.Add(new
                        {
                            guid = part.Identifier.GUID.ToString(),
                            name = part.Name,
                            profile = part.Profile.ProfileString,
                            material = part.Material.MaterialString,
                            @class = part.Class,
                            finish = part.Finish,
                            weight = Math.Round(weight, 3)
                        });
                    }
                }

                realOut.WriteLine(JsonSerializer.Serialize(results));
                return true;
            }

            case "select_by_class":
            {
                if (args.Length < 2)
                {
                    realOut.WriteLine("{\"error\":\"Missing class number\"}");
                    return true;
                }

                var className = args[1];
                var allObjs = model.GetModelObjectSelector().GetAllObjects();
                var toSelect = new ArrayList();
                while (allObjs.MoveNext())
                {
                    if (allObjs.Current is Tekla.Structures.Model.Part p && p.Class == className)
                        toSelect.Add(p);
                }

                new Tekla.Structures.Model.UI.ModelObjectSelector().Select(toSelect);
                realOut.WriteLine(JsonSerializer.Serialize(new { count = toSelect.Count, @class = className }));
                return true;
            }

            case "get_selected_weight":
            {
                var sel = new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects();
                double totalWeight = 0;
                int count = 0;
                while (sel.MoveNext())
                {
                    if (sel.Current is Tekla.Structures.Model.Part pt)
                    {
                        double w = 0;
                        pt.GetReportProperty("WEIGHT", ref w);
                        totalWeight += w;
                        count++;
                    }
                }

                realOut.WriteLine(JsonSerializer.Serialize(new { totalWeight = Math.Round(totalWeight, 2), count }));
                return true;
            }

            default:
                return false;
        }
    }
}
