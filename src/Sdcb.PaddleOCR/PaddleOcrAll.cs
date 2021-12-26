﻿using OpenCvSharp;
using Sdcb.PaddleInference;
using System;
using System.IO;
using System.Linq;

namespace Sdcb.PaddleOCR
{
    public class PaddleOcrAll : IDisposable
	{
		public PaddleOcrDetector Detector { get; }
		public PaddleOcrRecognizer Recognizer { get; }

		public PaddleOcrAll(string modelPath, string labelFilePath)
		{
			Detector = new(Path.Combine(modelPath, "det"));
			Recognizer = new(Path.Combine(modelPath, "rec"), labelFilePath);
		}

		public PaddleOcrAll(PaddleConfig detectorConfig, PaddleConfig recognizerConfig, string labelFilePath)
		{
			Detector = new(detectorConfig);
			Recognizer = new(recognizerConfig, labelFilePath);
		}

		public PaddleOcrResult Run(Mat src)
		{
			RotatedRect[] rects = Detector.Run(src);
			return new PaddleOcrResult(rects
				.Select(rect =>
				{
					using Mat roi = GetRotateCropImage(src, rect);
					PaddleOcrRecognizerResult result = Recognizer.Run(roi);
					PaddleOcrResultRegion region = new(rect, result.Text, result.Score);
					return region;
				})
				.ToArray());
		}

		public static Mat GetRotateCropImage(Mat src, RotatedRect rect)
		{
			bool wider = rect.Size.Width > rect.Size.Height;
			float angle = rect.Angle;
			Size srcSize = src.Size();
			Rect boundingRect = rect.BoundingRect();

			int expTop = Math.Max(0, 0 - boundingRect.Top);
			int expBottom = Math.Max(0, boundingRect.Bottom - srcSize.Height);
			int expLeft = Math.Max(0, 0 - boundingRect.Left);
			int expRight = Math.Max(0, boundingRect.Right - srcSize.Width);

			Rect rectToExp = boundingRect + new Point(expTop, expLeft);
			Rect roiRect = Rect.FromLTRB(
				boundingRect.Left + expLeft,
				boundingRect.Top + expTop,
				boundingRect.Right - expRight,
				boundingRect.Bottom - expBottom);
			using Mat boundingMat = src[roiRect];
			using Mat expanded = boundingMat.CopyMakeBorder(expTop, expBottom, expLeft, expRight, BorderTypes.Replicate);
			Point2f[] rp = rect.Points()
				.Select(v => new Point2f(v.X - rectToExp.X, v.Y - rectToExp.Y))
				.ToArray();

			Point2f[] srcPoints = (wider, angle) switch
			{
				(true, >= 0 and < 45) => new[] { rp[1], rp[2], rp[3], rp[0] },
				_ => new[] { rp[0], rp[3], rp[2], rp[1] }
			};

			var ptsDst0 = new Point2f(0, 0);
			var ptsDst1 = new Point2f(rect.Size.Width, 0);
			var ptsDst2 = new Point2f(rect.Size.Width, rect.Size.Height);
			var ptsDst3 = new Point2f(0, rect.Size.Height);

			using Mat matrix = Cv2.GetPerspectiveTransform(srcPoints, new[] { ptsDst0, ptsDst1, ptsDst2, ptsDst3 });

			Mat dest = expanded.WarpPerspective(matrix, new Size(rect.Size.Width, rect.Size.Height), InterpolationFlags.Nearest, BorderTypes.Replicate);

			if (!wider)
			{
				Cv2.Transpose(dest, dest);
			}
			else if (angle > 45)
			{
				Cv2.Flip(dest, dest, FlipMode.X);
			}
			return dest;
		}

		public void Dispose()
		{
			Detector.Dispose();
			Recognizer.Dispose();
		}
	}
}
