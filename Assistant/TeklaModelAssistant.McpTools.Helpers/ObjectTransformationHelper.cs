using System;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;

namespace TeklaModelAssistant.McpTools.Helpers
{
	public static class ObjectTransformationHelper
	{
		public static bool Move(ModelObject modelObject, Vector translationVector)
		{
			if (modelObject == null)
			{
				throw new ArgumentNullException("modelObject");
			}
			if (translationVector == null)
			{
				throw new ArgumentNullException("translationVector");
			}
			return Operation.MoveObject(modelObject, translationVector);
		}

		public static bool Rotate(ModelObject modelObject, Point axisPoint1, Point axisPoint2, double angleRadians)
		{
			if (modelObject == null)
			{
				throw new ArgumentNullException("modelObject");
			}
			if (axisPoint1 == null)
			{
				throw new ArgumentNullException("axisPoint1");
			}
			if (axisPoint2 == null)
			{
				throw new ArgumentNullException("axisPoint2");
			}
			if (Math.Abs(angleRadians) < 1E-12)
			{
				return true;
			}
			CoordinateSystem startCoordinateSystem = modelObject.GetCoordinateSystem();
			if (startCoordinateSystem == null)
			{
				return false;
			}
			Vector axisDirection = new Vector(axisPoint2.X - axisPoint1.X, axisPoint2.Y - axisPoint1.Y, axisPoint2.Z - axisPoint1.Z);
			if (axisDirection.GetLength() < 1E-06)
			{
				throw new ArgumentException("Axis points must not be the same.", "axisPoint2");
			}
			Matrix rotation = MatrixFactory.Rotate(angleRadians, axisDirection);
			Point rotatedOrigin = RotatePoint(startCoordinateSystem.Origin);
			Point rotatedAxisXPoint = RotatePoint(Translate(startCoordinateSystem.Origin, startCoordinateSystem.AxisX));
			Point rotatedAxisYPoint = RotatePoint(Translate(startCoordinateSystem.Origin, startCoordinateSystem.AxisY));
			Vector rotatedAxisX = new Vector(rotatedAxisXPoint.X - rotatedOrigin.X, rotatedAxisXPoint.Y - rotatedOrigin.Y, rotatedAxisXPoint.Z - rotatedOrigin.Z);
			Vector rotatedAxisY = new Vector(rotatedAxisYPoint.X - rotatedOrigin.X, rotatedAxisYPoint.Y - rotatedOrigin.Y, rotatedAxisYPoint.Z - rotatedOrigin.Z);
			CoordinateSystem endCoordinateSystem = new CoordinateSystem(rotatedOrigin, rotatedAxisX, rotatedAxisY);
			return Operation.MoveObject(modelObject, startCoordinateSystem, endCoordinateSystem);
			Point RotatePoint(Point point)
			{
				Point translated = new Point(point);
				translated.Translate(0.0 - axisPoint1.X, 0.0 - axisPoint1.Y, 0.0 - axisPoint1.Z);
				Point rotated = rotation.Transform(translated);
				rotated.Translate(axisPoint1.X, axisPoint1.Y, axisPoint1.Z);
				return rotated;
			}
		}

		public static bool Mirror(ModelObject modelObject, double x, double y, double angleRadians)
		{
			if (modelObject == null)
			{
				throw new ArgumentNullException("modelObject");
			}
			CoordinateSystem startCoordinateSystem = modelObject.GetCoordinateSystem();
			if (startCoordinateSystem == null)
			{
				return false;
			}
			Point mirrorLinePoint = new Point(x, y, 0.0);
			double cosAngle = Math.Cos(angleRadians);
			double sinAngle = Math.Sin(angleRadians);
			Vector mirrorLineDirection = new Vector(cosAngle, sinAngle, 0.0);
			Point mirroredOrigin = MirrorPointAcrossLine(startCoordinateSystem.Origin, mirrorLinePoint, mirrorLineDirection);
			Vector mirroredAxisX = MirrorVectorAcrossLine(startCoordinateSystem.AxisX, mirrorLineDirection);
			Vector mirroredAxisY = MirrorVectorAcrossLine(startCoordinateSystem.AxisY, mirrorLineDirection);
			CoordinateSystem endCoordinateSystem = new CoordinateSystem(mirroredOrigin, mirroredAxisX, mirroredAxisY);
			return Operation.MoveObject(modelObject, startCoordinateSystem, endCoordinateSystem);
		}

		private static Point MirrorPointAcrossLine(Point point, Point linePoint, Vector lineDirection)
		{
			Vector toPoint = new Vector(point.X - linePoint.X, point.Y - linePoint.Y, point.Z - linePoint.Z);
			double dotProduct = toPoint.X * lineDirection.X + toPoint.Y * lineDirection.Y;
			Vector projectionOnLine = new Vector(dotProduct * lineDirection.X, dotProduct * lineDirection.Y, 0.0);
			Vector perpendicular = new Vector(toPoint.X - projectionOnLine.X, toPoint.Y - projectionOnLine.Y, 0.0);
			return new Point(point.X - 2.0 * perpendicular.X, point.Y - 2.0 * perpendicular.Y, point.Z);
		}

		private static Vector MirrorVectorAcrossLine(Vector vector, Vector lineDirection)
		{
			double dotProduct = vector.X * lineDirection.X + vector.Y * lineDirection.Y;
			Vector projectionOnLine = new Vector(dotProduct * lineDirection.X, dotProduct * lineDirection.Y, 0.0);
			Vector perpendicular = new Vector(vector.X - projectionOnLine.X, vector.Y - projectionOnLine.Y, 0.0);
			return new Vector(vector.X - 2.0 * perpendicular.X, vector.Y - 2.0 * perpendicular.Y, vector.Z);
		}

		private static Point Translate(Point point, Vector vector)
		{
			Point result = new Point(point);
			result.Translate(vector.X, vector.Y, vector.Z);
			return result;
		}
	}
}
