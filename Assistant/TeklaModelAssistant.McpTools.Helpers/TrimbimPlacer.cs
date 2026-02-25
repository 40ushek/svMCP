using System;
using System.IO;
using System.Runtime.InteropServices;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace TeklaModelAssistant.McpTools.Helpers
{
	internal class TrimbimPlacer
	{
		private struct Transform
		{
			private double ox;

			private double oy;

			private double oz;

			private double xx;

			private double xy;

			private double xz;

			private double yx;

			private double yy;

			private double yz;

			public void SetPosition(Point origin, Vector axisX, Vector axisY)
			{
				ox = origin.X / 1000.0;
				oy = origin.Y / 1000.0;
				oz = origin.Z / 1000.0;
				Point p2 = origin + axisX;
				Point p3 = origin + axisY;
				xx = p2.X;
				xy = p2.Y;
				xz = p2.Z;
				yx = p3.X;
				yy = p3.Y;
				yz = p3.Z;
			}

			public static Transform Identity()
			{
				return new Transform
				{
					ox = 0.0,
					oy = 0.0,
					oz = 0.0,
					xx = 1.0,
					xy = 0.0,
					xz = 0.0,
					yx = 0.0,
					yy = 1.0,
					yz = 0.0
				};
			}
		}

		private const string DllName = "TeklaStructures_application.dll";

		[DllImport("TeklaStructures_application.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern int RenderTrimbim(byte[] bytes, int size, Transform transform);

		[DllImport("TeklaStructures_application.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern void DeleteTrimbim(int handle);

		[DllImport("TeklaStructures_application.dll", CallingConvention = CallingConvention.StdCall)]
		private static extern void MoveTrimbim(int handle, Transform transform);

		public static int LoadAndPlaceTrimbim(string trimbimPath, Point position, Vector axisX = null, Vector axisY = null)
		{
			if (string.IsNullOrWhiteSpace(trimbimPath))
			{
				Console.WriteLine("TrimBim path is null or empty.");
				return 0;
			}
			try
			{
				if (axisX == null)
				{
					axisX = new Vector(1.0, 0.0, 0.0);
				}
				if (axisY == null)
				{
					axisY = new Vector(0.0, 1.0, 0.0);
				}
				Model model = new Model();
				string modelPath = model.GetInfo().ModelPath;
				if (!File.Exists(trimbimPath))
				{
					Console.WriteLine("TrimBim file not found: " + trimbimPath);
					return 0;
				}
				byte[] trimbimBytes = File.ReadAllBytes(trimbimPath);
				if (trimbimBytes.Length == 0)
				{
					Console.WriteLine("TrimBim file is empty: " + trimbimPath);
					return 0;
				}
				Transform transform = default(Transform);
				transform.SetPosition(position, axisX, axisY);
				int handle = RenderTrimbim(trimbimBytes, trimbimBytes.Length, transform);
				if (handle > 0)
				{
					Console.WriteLine($"Successfully placed TrimBim '{trimbimPath}' at position ({position.X}, {position.Y}, {position.Z})");
				}
				else
				{
					Console.WriteLine("Failed to render TrimBim '" + trimbimPath + "'");
				}
				return handle;
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error loading TrimBim: " + ex.Message);
				return 0;
			}
		}

		public static void MoveTrimbimTo(int handle, Point newPosition, Vector axisX = null, Vector axisY = null)
		{
			if (handle <= 0)
			{
				return;
			}
			try
			{
				if (axisX == null)
				{
					axisX = new Vector(1.0, 0.0, 0.0);
				}
				if (axisY == null)
				{
					axisY = new Vector(0.0, 1.0, 0.0);
				}
				Transform transform = default(Transform);
				transform.SetPosition(newPosition, axisX, axisY);
				MoveTrimbim(handle, transform);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error moving TrimBim: " + ex.Message);
			}
		}

		public static void RemoveTrimbim(int handle)
		{
			if (handle <= 0)
			{
				return;
			}
			try
			{
				DeleteTrimbim(handle);
				Console.WriteLine($"Removed TrimBim with handle {handle}");
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error removing TrimBim: " + ex.Message);
			}
		}

		public static string GetTrimbimPath(string trimbimId)
		{
			Model model = new Model();
			return Path.Combine(model.GetInfo().ModelPath, "Trimbims", trimbimId + ".trb");
		}

		public static bool TrimbimExists(string trimbimId)
		{
			return File.Exists(GetTrimbimPath(trimbimId));
		}
	}
}
