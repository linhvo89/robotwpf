using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WpfCompanyApp.Models;

namespace WpfCompanyApp.CalibRobot
{
    public sealed class Affine2D
    {
        // X = a*u + b*v + c
        // Y = d*u + e*v + f
        public double a, b, c, d, e, f;

        public (double X, double Y) PixelToRobot(double u, double v)
            => (a * u + b * v + c, d * u + e * v + f);

        public static Affine2D FitFromCalibPoints(IReadOnlyList<RobotPointCalib> pts)
        {
            if (pts == null) throw new ArgumentNullException(nameof(pts));
            if (pts.Count < 3) throw new ArgumentException("Need >= 3 points.", nameof(pts));

            double[,] ATA = new double[3, 3];
            double[] ATX = new double[3];
            double[] ATY = new double[3];

            for (int i = 0; i < pts.Count; i++)
            {
                double u = pts[i].ImageX;
                double v = pts[i].ImageY;
                double X = pts[i].RobotX;
                double Y = pts[i].RobotY;

                double r0 = u, r1 = v, r2 = 1.0;

                ATA[0, 0] += r0 * r0; ATA[0, 1] += r0 * r1; ATA[0, 2] += r0 * r2;
                ATA[1, 0] += r1 * r0; ATA[1, 1] += r1 * r1; ATA[1, 2] += r1 * r2;
                ATA[2, 0] += r2 * r0; ATA[2, 1] += r2 * r1; ATA[2, 2] += r2 * r2;

                ATX[0] += r0 * X; ATX[1] += r1 * X; ATX[2] += r2 * X;
                ATY[0] += r0 * Y; ATY[1] += r1 * Y; ATY[2] += r2 * Y;
            }

            double[] pX = Solve3x3(ATA, ATX); // a b c
            double[] pY = Solve3x3(ATA, ATY); // d e f

            return new Affine2D { a = pX[0], b = pX[1], c = pX[2], d = pY[0], e = pY[1], f = pY[2] };
        }

        private static double[] Solve3x3(double[,] M, double[] b)
        {
            double[,] A = new double[3, 4];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++) A[r, c] = M[r, c];
                A[r, 3] = b[r];
            }

            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                double max = Math.Abs(A[col, col]);
                for (int r = col + 1; r < 3; r++)
                {
                    double v = Math.Abs(A[r, col]);
                    if (v > max) { max = v; pivot = r; }
                }
                if (max < 1e-12) throw new InvalidOperationException("Singular system.");

                if (pivot != col)
                    for (int c = col; c < 4; c++)
                        (A[col, c], A[pivot, c]) = (A[pivot, c], A[col, c]);

                double diag = A[col, col];
                for (int c = col; c < 4; c++) A[col, c] /= diag;

                for (int r = 0; r < 3; r++)
                {
                    if (r == col) continue;
                    double factor = A[r, col];
                    for (int c = col; c < 4; c++) A[r, c] -= factor * A[col, c];
                }
            }

            return new[] { A[0, 3], A[1, 3], A[2, 3] };
        }
    }
}
