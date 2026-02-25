using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TeklaModelAssistant.McpTools.Models
{
	public class TeklaPropertySet
	{
		[JsonPropertyName("lineColor")]
		public int? LineColor { get; set; }

		[JsonPropertyName("visibleLineColor")]
		public int? VisibleLineColor { get; set; }

		[JsonPropertyName("hiddenLineColor")]
		public int? HiddenLineColor { get; set; }

		[JsonPropertyName("sectionLineColor")]
		public int? SectionLineColor { get; set; }

		[JsonPropertyName("referenceLineColor")]
		public int? ReferenceLineColor { get; set; }

		[JsonPropertyName("lineType")]
		public int? LineType { get; set; }

		[JsonPropertyName("fillPattern")]
		public string FillPattern { get; set; }

		[JsonPropertyName("transparency")]
		public int? Transparency { get; set; }

		[JsonPropertyName("customProperties")]
		public Dictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();

		public Dictionary<string, object> ToPropertyDictionary()
		{
			Dictionary<string, object> dict = new Dictionary<string, object>();
			if (LineColor.HasValue)
			{
				dict["LineColor"] = LineColor.Value;
			}
			if (VisibleLineColor.HasValue)
			{
				dict["VisibleLineColor"] = VisibleLineColor.Value;
			}
			if (HiddenLineColor.HasValue)
			{
				dict["HiddenLineColor"] = HiddenLineColor.Value;
			}
			if (SectionLineColor.HasValue)
			{
				dict["SectionLineColor"] = SectionLineColor.Value;
			}
			if (ReferenceLineColor.HasValue)
			{
				dict["ReferenceLineColor"] = ReferenceLineColor.Value;
			}
			if (LineType.HasValue)
			{
				dict["LineType"] = LineType.Value;
			}
			if (!string.IsNullOrEmpty(FillPattern))
			{
				dict["FillPattern"] = FillPattern;
			}
			if (Transparency.HasValue)
			{
				dict["Transparency"] = Transparency.Value;
			}
			if (CustomProperties != null)
			{
				foreach (KeyValuePair<string, object> kvp in CustomProperties)
				{
					dict[kvp.Key] = kvp.Value;
				}
			}
			return dict;
		}
	}
}
