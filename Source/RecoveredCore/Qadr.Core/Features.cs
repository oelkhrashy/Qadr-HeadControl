using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public static class Features
{
	public static Observation? Extract(VisionResult r)
	{
		if (!double.IsFinite(r.Captured))
		{
			return null;
		}
		FaceGeometry faceGeometry = r.Geometry;
		if ((object)faceGeometry == null)
		{
			double[][] landmarks = r.Landmarks;
			if (landmarks == null || landmarks.Length < 264)
			{
				return null;
			}
			double num = double.PositiveInfinity;
			double num2 = num;
			double num3 = double.NegativeInfinity;
			double num4 = num3;
			int num5 = 0;
			double[][] array = landmarks;
			foreach (double[] array2 in array)
			{
				if (array2 == null || array2.Length < 3)
				{
					return null;
				}
				double[] array3 = array2;
				for (int j = 0; j < array3.Length; j++)
				{
					if (!double.IsFinite(array3[j]))
					{
						return null;
					}
				}
				num = Math.Min(num, array2[0]);
				num3 = Math.Max(num3, array2[0]);
				num2 = Math.Min(num2, array2[1]);
				num4 = Math.Max(num4, array2[1]);
				if (array2[0] < 0.0 || array2[0] > 1.0 || array2[1] < 0.0 || array2[1] > 1.0)
				{
					num5++;
				}
			}
			double num6 = landmarks[33][0] - landmarks[263][0];
			double num7 = landmarks[33][1] - landmarks[263][1];
			faceGeometry = new FaceGeometry((landmarks[33][0] + landmarks[263][0]) / 2.0, (landmarks[33][1] + landmarks[263][1]) / 2.0, Math.Sqrt(num6 * num6 + num7 * num7), landmarks[1][0], landmarks[1][1], num, num2, num3, num4, (double)num5 / (double)landmarks.Length);
		}
		if (faceGeometry.IsValid)
		{
			Dictionary<string, double>? blendshapes = r.Blendshapes;
			if (blendshapes == null || !blendshapes.Values.Any((double v) => !double.IsFinite(v)))
			{
				double eyeX = faceGeometry.EyeX;
				double eyeY = faceGeometry.EyeY;
				double eyeScale = faceGeometry.EyeScale;
				double num8 = (faceGeometry.NoseX - eyeX) / eyeScale;
				double pitch = (eyeY - faceGeometry.NoseY) / eyeScale;
				double[] array3 = r.Matrix;
				if (array3 != null && array3.Length > 0 && (r.Matrix.Length != 16 || !r.Matrix.All(double.IsFinite)))
				{
					return null;
				}
				double[] matrix = r.Matrix;
				if (matrix != null && matrix.Length == 16 && matrix.All(double.IsFinite))
				{
					num8 = 0.0 - Math.Atan2(matrix[8], Math.Sqrt(matrix[0] * matrix[0] + matrix[4] * matrix[4]));
					pitch = Math.Atan2(0.0 - matrix[9], matrix[10]);
				}
				eyeScale /= Math.Clamp(Math.Cos(num8), 0.6, 1.0);
				Observation observation = new Observation(num8, pitch, (Score("mouthSmileLeft") + Score("mouthSmileRight")) / 2.0, Score("jawOpen"), Math.Min(Score("eyeBlinkLeft"), Score("eyeBlinkRight")), r.Captured, eyeX, eyeY, eyeScale, Score("eyeBlinkLeft"), Score("eyeBlinkRight"), (faceGeometry.Left + faceGeometry.Right) / 2.0, (faceGeometry.Top + faceGeometry.Bottom) / 2.0, faceGeometry.Right - faceGeometry.Left, faceGeometry.Bottom - faceGeometry.Top, Math.Max(Score("browInnerUp"), (Score("browOuterUpLeft") + Score("browOuterUpRight")) / 2.0), Score("mouthPucker"));
				if (!observation.IsUsable)
				{
					return null;
				}
				MovementLabTrace.RecordPose(r,faceGeometry);
				return observation;
			}
		}
		return null;
		double Score(string key)
		{
			return Math.Clamp(r.Blendshapes?.GetValueOrDefault(key) ?? 0.0, 0.0, 1.0);
		}
	}
}
