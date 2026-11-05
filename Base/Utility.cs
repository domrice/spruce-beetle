/*
 * MIT License
 * 
 * Copyright (c) 2022 Dominik Reisach
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */


using System;
using System.Linq;
using System.Collections.Generic;
using Rhino.Geometry;


namespace SpruceBeetle
{
    public class Utility
    {
        //------------------------------------------------------------
        // Icon method
        //------------------------------------------------------------
        // loads a component icon straight from the embedded PNG stream. going through
        // Properties.Resources instead would deserialize the bitmap via BinaryFormatter,
        // which mangles the pixel format under Mono (Rhino on macOS). the copy detaches
        // the bitmap from the stream (GDI+ needs it open otherwise) and forces 32bppArgb
        public static System.Drawing.Bitmap Icon(string name)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("SpruceBeetle.Resources." + name + ".png"))
            using (var png = new System.Drawing.Bitmap(stream))
                return new System.Drawing.Bitmap(png);
        }


        //------------------------------------------------------------
        // CurveCurvature method
        //------------------------------------------------------------
        public static Interval CurveCurvature(Curve crv)
        {
            const int accuracy = 400;

            // sample over the curve's own domain - callers must not be required to reparameterize
            List<double> lengthList = new List<double>();
            for (int i = 0; i <= accuracy; i++)
                lengthList.Add(crv.CurvatureAt(crv.Domain.ParameterAt((double)i / accuracy)).Length);

            // return the curvature interval
            return new Interval(lengthList.Min(), lengthList.Max());
        }


        //------------------------------------------------------------
        // SecondPlaneAt method
        //------------------------------------------------------------
        // Frame that closes an Offcut of length Z started at firstPlane. The Offcut is built on
        // the average of both frames and trimmed by them, so two non-parallel end planes eat
        // length at the corners: with opening angle a, a corner sitting r to the outside of the
        // curve costs r * tan(a/2) per end. Usable chord is therefore Z - 2 * r * tan(a/2).
        // Both terms grow with t, so the residual is monotonic and bisection solves it.
        // r is the outermost corner of the actual base rectangle, not the half-diagonal: with a
        // base position other than centred the section sits off-axis, and using the half-diagonal
        // there under-counts the loss, leaving the outer faces of neighbouring Offcuts stepped.
        public static Plane SecondPlaneAt(Curve crv, Plane firstPlane, Offcut offcutDim, int ocBaseIndex)
        {
            crv.ClosestPoint(firstPlane.Origin, out double lo);

            // away from the centre of curvature; on a straight stretch the angle is zero, so the
            // fallback value never reaches the result
            Vector3d outward = -crv.CurvatureAt(lo);
            Rectangle3d baseRect = Offcut.GetOffcutBase(offcutDim.X, offcutDim.Y, firstPlane, ocBaseIndex);

            double radius = outward.Unitize()
                ? Enumerable.Range(0, 4).Max(i => (baseRect.Corner(i) - firstPlane.Origin) * outward)
                : Math.Sqrt(offcutDim.X * offcutDim.X + offcutDim.Y * offcutDim.Y) / 2;
            double hi = crv.Domain.T1;

            // ponytail: fixed 60 bisection steps rather than a tolerance loop - that is past
            // double precision on any curve domain. Upgrade path: Brent, if this ever gets hot.
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                crv.PerpendicularFrameAt(mid, out Plane frame);

                double angle = Vector3d.VectorAngle(firstPlane.ZAxis, frame.ZAxis);
                double needed = firstPlane.Origin.DistanceTo(frame.Origin) + 2 * radius * Math.Tan(angle / 2);

                if (needed < offcutDim.Z)
                    lo = mid;
                else
                    hi = mid;
            }

            crv.PerpendicularFrameAt(lo, out Plane secondPlane);
            return secondPlane;
        }


        //------------------------------------------------------------
        // SortedIndices method
        //------------------------------------------------------------
        // Positions of the count smallest values, or largest when descending. Sorting the values
        // and recovering positions with IndexOf hands back the same position twice as soon as two
        // values are equal - normal for a symmetric intersection - silently dropping one piece and
        // duplicating another. OrderBy is stable, so ties fall back to the original order.
        public static List<int> SortedIndices(List<double> values, int count, bool descending = false)
        {
            IEnumerable<int> order = Enumerable.Range(0, values.Count);

            return (descending ? order.OrderByDescending(i => values[i]) : order.OrderBy(i => values[i]))
                .Take(count).ToList();
        }


        //------------------------------------------------------------
        // Remap method
        //------------------------------------------------------------
        public static double Remap(double targetValue, Interval sourceBounds, Interval targetBounds)
        {
            // define target value
            double normValue = (targetValue - sourceBounds.T0) / sourceBounds.Length;
            double newValue = targetBounds.T0 + normValue * targetBounds.Length;

            return newValue;
        }


        //------------------------------------------------------------
        // StartPlane method
        //------------------------------------------------------------
        // Frame the next Offcut starts on: the end frame of the one before it, or - for the very
        // first Offcut - the frame at the start of the curve, rotated by the start angle.
        public static Plane StartPlane(Curve crv, List<Plane> previousPlanes, Interval angleBounds)
        {
            if (previousPlanes.Any())
                return previousPlanes.Last();

            crv.PerpendicularFrameAt(crv.Domain.T0, out Plane firstPlane);

            double angle = Remap(0.0, new Interval(0.0, 1.0), angleBounds);
            firstPlane.Transform(Transform.Rotation(ConvertToRadians(angle), firstPlane.ZAxis, firstPlane.Origin));

            return firstPlane;
        }


        //------------------------------------------------------------
        // CurvatureIndex method
        //------------------------------------------------------------
        // Position of the Offcut whose length best matches the curvature at t: the tightest spot
        // on the curve maps to the shortest Offcut, the straightest to the longest.
        public static int CurvatureIndex(Curve crv, Interval curveBounds, List<Offcut> offcutDim, double t)
        {
            List<double> dzList = offcutDim.Select(oc => oc.Z).ToList();
            Interval dBounds = new Interval(dzList.Max(), dzList.Min());

            double newValue = Remap(crv.CurvatureAt(t).Length, curveBounds, dBounds);
            double closestNum = dzList.Aggregate((x, y) => Math.Abs(x - newValue) < Math.Abs(y - newValue) ? x : y);

            return dzList.IndexOf(closestNum);
        }


        //------------------------------------------------------------
        // Placement type
        //------------------------------------------------------------
        // One placed Offcut and the four frames that describe it. Returned as a whole so callers
        // stop indexing a List<Plane> by positional convention.
        public class Placement
        {
            public Brep Geometry;
            public Plane FirstPlane;
            public Plane SecondPlane;
            public Plane AveragePlane;
            public Plane MovedAveragePlane;
            public double Volume;
        }


        //------------------------------------------------------------
        // PlaceOffcut method
        //------------------------------------------------------------
        // Builds the Offcut box on the average of its two end frames. trim cuts it back to those
        // frames; on a straight curve the intermediate Offcuts already meet flush, so only the
        // closing one needs it.
        public static Placement PlaceOffcut(Curve crv, Offcut offcutDim, Plane firstPlane, double angle, int ocBaseIndex, bool end, bool trim)
        {
            Plane secondPlane;

            if (end)
                crv.PerpendicularFrameAt(crv.Domain.T1, out secondPlane);
            else
                secondPlane = SecondPlaneAt(crv, firstPlane, offcutDim, ocBaseIndex);

            // determine the rotation of the Offcuts
            secondPlane.Transform(Transform.Rotation(ConvertToRadians(angle), secondPlane.ZAxis, secondPlane.Origin));

            // create average plane out of the first and second one
            Point3d originPt = new Point3d((firstPlane.Origin + secondPlane.Origin) / 2);
            Plane averagePlane = new Plane(originPt, (firstPlane.XAxis + secondPlane.XAxis) / 2, (firstPlane.YAxis + secondPlane.YAxis) / 2);

            // copy average plane and move to the center of the to be constructed Offcut - it is needed for further operations
            Plane movedAvrgPlane = new Plane(averagePlane)
            {
                Origin = Offcut.GetOffcutBase(offcutDim.X, offcutDim.Y, averagePlane, ocBaseIndex).Center
            };

            Brep offcut = Offcut.CreateOffcutBrep(offcutDim, averagePlane, ocBaseIndex);

            if (trim)
            {
                firstPlane.Flip();
                Brep firstCap = offcut.Trim(firstPlane, 0.0001)[0].CapPlanarHoles(0.0001);
                firstPlane.Flip();

                offcut = firstCap.Trim(secondPlane, 0.0001)[0].CapPlanarHoles(0.0001);
                offcut.Faces.SplitKinkyFaces(0.0001);
            }

            return new Placement
            {
                Geometry = offcut,
                FirstPlane = firstPlane,
                SecondPlane = secondPlane,
                AveragePlane = averagePlane,
                MovedAveragePlane = movedAvrgPlane,
                Volume = offcut.GetVolume()
            };
        }


        //------------------------------------------------------------
        // TrimWaste method
        //------------------------------------------------------------
        // Material lost trimming the placed Offcuts back to their end planes. Vol is the nominal
        // X * Y * Z of the stock piece, FabVol the volume that survived the trim, so the
        // difference is what the two non-parallel end cuts took off. Unused Offcuts are not waste
        // - they go back to the inventory - so they are not counted here.
        public static double TrimWaste(IEnumerable<Offcut> placed)
        {
            return placed.Sum(oc => oc.Vol - oc.FabVol);
        }


        //------------------------------------------------------------
        // CurveExitsOffcut method
        //------------------------------------------------------------
        // True when the curve leaves the placed Offcut. The Offcut is a straight box trimmed at
        // two end planes, so on a tight enough radius the curve bows out through its side: the
        // geometry is still valid, and still wrong. Testing the sampled curve against the placed
        // Brep catches this for every base position, not just the centred one, and needs no
        // tolerance constant of its own.
        // ponytail: 9 samples between the two end planes. Deviation peaks at the middle and is
        // monotonic either side of it, so a coarse sample cannot miss a real excursion by much.
        // Upgrade path: bisect towards the peak if a near-miss ever has to be caught exactly.
        public static bool CurveExitsOffcut(Curve crv, Offcut offcut)
        {
            const int samples = 9;

            if (offcut.OffcutGeometry == null || !offcut.OffcutGeometry.IsSolid)
                return false;

            crv.ClosestPoint(offcut.FirstPlane.Origin, out double t0);
            crv.ClosestPoint(offcut.SecondPlane.Origin, out double t1);

            for (int i = 1; i < samples; i++)
            {
                Point3d pt = crv.PointAt(t0 + (t1 - t0) * i / samples);

                if (!offcut.OffcutGeometry.IsPointInside(pt, 0.0001, true))
                    return true;
            }

            return false;
        }


        //------------------------------------------------------------
        // MinimumDimensions method
        //------------------------------------------------------------
        public static double[] MinimumDimensions(List<Offcut> offcutData)
        {
            // empty lists to store Offcut x and y dimensions
            List<double> x = new List<double>();
            List<double> y = new List<double>();

            // store Offcut dimensions in list
            foreach (Offcut offcut in offcutData)
            {
                x.Add(offcut.X);
                y.Add(offcut.Y);
            }

            // get min and max values
            double minX = x.Min();
            double minY = y.Min();

            // create and return an array
            double[] minimumDimensions = { minX, minY };

            return minimumDimensions;
        }


        //------------------------------------------------------------
        // Degrees to radians method
        //------------------------------------------------------------
        public static double ConvertToRadians(double angle)
        {
            // return degrees to radians value
            return (Math.PI / 180) * angle;
        }


        //------------------------------------------------------------
        // GetMaxZ method
        //------------------------------------------------------------
        public static void GetMaxZ(List<Offcut> offcutList, out Offcut maxOffcut, out int maxIndex)
        {
            // initialise new list
            List<double> zvalues = new List<double>();

            // store all z values in list
            foreach (Offcut offcut in offcutList)
                zvalues.Add(offcut.Z);

            // get index of Offcut
            maxIndex = zvalues.IndexOf(zvalues.Max());

            // return Offcut with max z-value
            maxOffcut = offcutList[maxIndex];
        }


        //------------------------------------------------------------
        // GetMinimumDimensions method
        //------------------------------------------------------------
        public static List<double[]> GetMinimumDimension(List<Offcut> offcutList, int i)
        {
            // get the base for the joint position of each Offcut
            double[] firstMin = new double[2];
            double[] secondMin = new double[2];

            if (i == 0)
            {
                firstMin = Joint.GetMinValue(offcutList[i].X, offcutList[i].Y, offcutList[offcutList.Count - 1].X, offcutList[offcutList.Count - 1].Y);
                secondMin = Joint.GetMinValue(offcutList[i].X, offcutList[i].Y, offcutList[i + 1].X, offcutList[i + 1].Y);
            }

            else if (0 < i && i < offcutList.Count - 1)
            {
                firstMin = Joint.GetMinValue(offcutList[i].X, offcutList[i].Y, offcutList[i - 1].X, offcutList[i - 1].Y);
                secondMin = Joint.GetMinValue(offcutList[i].X, offcutList[i].Y, offcutList[i + 1].X, offcutList[i + 1].Y);
            }

            else
            {
                firstMin = Joint.GetMinValue(offcutList[i].X, offcutList[i].Y, offcutList[i - 1].X, offcutList[i - 1].Y);
                secondMin = Joint.GetMinValue(offcutList[i].X, offcutList[i].Y, offcutList[0].X, offcutList[0].Y);
            }

            List<double[]> returnList = new List<double[]> { firstMin, secondMin };
            return returnList;
        }


        //------------------------------------------------------------
        // GetOffcutBaseCurve method
        //------------------------------------------------------------
        public static Curve GetOffcutBaseCurve(List<Offcut> offcutList)
        {
            // create new list of points
            List<Point3d> originList = new List<Point3d>();

            // call MinimumDimensions method
            double[] dimensions = MinimumDimensions(offcutList);

            for (int i = 0; i < offcutList.Count; i++)
            {
                // initialise new planes
                Plane firstPlane = new Plane(offcutList[i].FirstPlane);

                // setting new origin point for the planes
                Rectangle3d firstRect = Offcut.GetOffcutBase(dimensions[0], dimensions[1], firstPlane, offcutList[i].PositionIndex);

                // add center points to point list
                originList.Add(firstRect.Center);

                // include the last plane
                if (i == offcutList.Count - 1)
                    originList.Add(Offcut.GetOffcutBase(dimensions[0], dimensions[1], offcutList[i].SecondPlane, offcutList[i].PositionIndex).Center);
            }

            // create curve by interpolation
            Curve baseCurve = Curve.CreateInterpolatedCurve(originList, 3);

            // return curve
            return baseCurve;
        }
    }
}
