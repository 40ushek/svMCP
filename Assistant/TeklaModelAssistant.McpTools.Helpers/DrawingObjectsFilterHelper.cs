using System;
using System.Collections.Generic;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.DrawingInternal;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public static class DrawingObjectsFilterHelper
	{
		public static List<int> FindDrawingObjectsIds(Drawing activeDrawing, Model model, string objectType, string specificType)
		{
			Type targetType = Type.GetType("Tekla.Structures.Drawing." + objectType + ", Tekla.Structures.Drawing");
			if (targetType == null)
			{
				return null;
			}
			string name = targetType.Name;
			string text = name;
			Func<DrawingObject, bool> predicate = ((!(text == "Mark")) ? ((Func<DrawingObject, bool>)((DrawingObject obj) => targetType.IsInstanceOfType(obj))) : ((Func<DrawingObject, bool>)delegate(DrawingObject obj)
			{
				Mark mark = obj as Mark;
				return string.IsNullOrEmpty(specificType) || GetMarkType(mark, model).Equals(specificType, StringComparison.OrdinalIgnoreCase);
			}));
			List<int> foundObjects = new List<int>();
			DrawingObjectEnumerator enumerator = activeDrawing.GetSheet().GetAllObjects(targetType);
			while (enumerator.MoveNext())
			{
				DrawingObject currentObject = enumerator.Current;
				if (predicate(currentObject))
				{
					foundObjects.Add(currentObject.GetIdentifier().ID);
				}
			}
			return foundObjects;
		}

		public static List<DrawingObject> FindDrawingObjects(Drawing activeDrawing, Model model, string objectType, string specificType)
		{
			Type targetType = Type.GetType("Tekla.Structures.Drawing." + objectType + ", Tekla.Structures.Drawing");
			if (targetType == null)
			{
				return null;
			}
			string name = targetType.Name;
			string text = name;
			Func<DrawingObject, bool> predicate = ((!(text == "Mark")) ? ((Func<DrawingObject, bool>)((DrawingObject obj) => targetType.IsInstanceOfType(obj))) : ((Func<DrawingObject, bool>)delegate(DrawingObject obj)
			{
				Mark mark = obj as Mark;
				return string.IsNullOrEmpty(specificType) || GetMarkType(mark, model).Equals(specificType, StringComparison.OrdinalIgnoreCase);
			}));
			List<DrawingObject> foundObjects = new List<DrawingObject>();
			DrawingObjectEnumerator enumerator = activeDrawing.GetSheet().GetAllObjects(targetType);
			while (enumerator.MoveNext())
			{
				DrawingObject currentObject = enumerator.Current;
				if (predicate(currentObject))
				{
					foundObjects.Add(currentObject);
				}
			}
			return foundObjects;
		}

		public static List<DrawingObject> GetSelectedDrawingObjects()
		{
			List<DrawingObject> drawingObjectsList = new List<DrawingObject>();
			DrawingHandler drawingHandler = new DrawingHandler();
			if (drawingHandler.GetActiveDrawing() == null)
			{
				return drawingObjectsList;
			}
			DrawingObjectSelector drawingObjectSelector = drawingHandler.GetDrawingObjectSelector();
			DrawingObjectEnumerator enumerator = drawingObjectSelector.GetSelected();
			while (enumerator.MoveNext())
			{
				DrawingObject selectedObject = enumerator.Current;
				if (selectedObject != null)
				{
					drawingObjectsList.Add(selectedObject);
				}
			}
			return drawingObjectsList;
		}

		private static string GetMarkType(Mark mark, Model model)
		{
			DrawingObjectEnumerator associatedObjects = mark.GetRelatedObjects();
			foreach (object associatedObject in associatedObjects)
			{
				if (!(associatedObject is Tekla.Structures.Drawing.ModelObject drawingModelObject))
				{
					continue;
				}
				Tekla.Structures.Model.ModelObject modelObject = model.SelectModelObject(drawingModelObject.ModelIdentifier);
				if (modelObject != null)
				{
					if (modelObject is Tekla.Structures.Model.Part)
					{
						return "Part Mark";
					}
					if (modelObject is BoltGroup)
					{
						return "Bolt Mark";
					}
					if (modelObject is RebarGroup || modelObject is SingleRebar)
					{
						return "Reinforcement Mark";
					}
					if (modelObject is Tekla.Structures.Model.Weld)
					{
						return "Weld Mark";
					}
					if (modelObject is Assembly)
					{
						return "Assembly Mark";
					}
					if (modelObject is Tekla.Structures.Model.Connection)
					{
						return "Connection Mark";
					}
				}
			}
			return "Unknown Mark Type";
		}
	}
}
