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
using System.Collections.Generic;
using Rhino.Geometry;


namespace SpruceBeetle.Alignment
{
    // The consume-the-Offcut-list loop shared by Curve Alignment, Optimized Curve Alignment and
    // Test Alignment. Those components run the same algorithm and differ only in which Offcut is
    // picked next, which is what they hand in as a picker.
    public static class AlignmentRun
    {
        // Given the Offcuts still unplaced and the curve parameter of the current start plane,
        // return the position in that list of the Offcut to place next.
        public delegate int OffcutPicker(List<Offcut> remaining, double t);


        // Places Offcuts on each curve in turn, in the order the curves were given: the first
        // curve gets first pick of the inventory and every later one sees only what is left.
        // offcutData is consumed across all of them, so on return it holds the Offcuts no curve
        // used. Returns one list of placed Offcuts per curve, in the same order.
        public static List<List<Offcut>> RunAll(List<Curve> curves, List<Offcut> offcutData, Interval angleBounds, int ocBaseIndex, Func<Curve, OffcutPicker> pickerFor)
        {
            List<List<Offcut>> placed = new List<List<Offcut>>();

            foreach (Curve curve in curves)
            {
                // work on a copy - reparameterizing would otherwise mutate the input geometry
                Curve crv = curve.DuplicateCurve();
                crv.Domain = new Interval(0.0, 1.0);

                placed.Add(Run(crv, offcutData, angleBounds, ocBaseIndex, pickerFor(crv)));
            }

            return placed;
        }


        // Places Offcuts head to tail along crv until the curve is full. offcutData is consumed:
        // whatever is left in it on return is the unused Offcuts.
        public static List<Offcut> Run(Curve crv, List<Offcut> offcutData, Interval angleBounds, int ocBaseIndex, OffcutPicker pick)
        {
            bool linear = crv.IsLinear();

            List<Offcut> placed = new List<Offcut>();
            List<Plane> secondPlanes = new List<Plane>();

            double tIntersect = 0.0;
            double distance = crv.PointAtStart.DistanceTo(crv.PointAtEnd);

            for (int i = offcutData.Count; i > 0; i--)
            {
                Utility.GetMaxZ(offcutData, out Offcut maxOffcut, out int maxIndex);

                // Once the longest Offcut left no longer fits in the remaining curve, the curve
                // cannot take another full-length piece, so this one closes the alignment and is
                // trimmed at the curve end. The closing piece is the shortest Offcut that still
                // spans the gap - anything longer is trimmed away as waste, anything shorter
                // leaves the tail of the curve uncovered.
                bool end = distance <= maxOffcut.Z;
                int index = end ? ClosingIndex(offcutData, distance, maxIndex) : pick(offcutData, tIntersect);

                Offcut dim = offcutData[index];
                double angle = Utility.Remap(tIntersect, new Interval(0.0, 1.0), angleBounds);
                Plane startPlane = Utility.StartPlane(crv, secondPlanes, angleBounds);

                Utility.Placement placement = Utility.PlaceOffcut(crv, dim, startPlane, angle, ocBaseIndex, end, !linear || end);

                placed.Add(new Offcut(dim)
                {
                    OffcutGeometry = placement.Geometry,
                    FabVol = placement.Volume,
                    FirstPlane = placement.FirstPlane,
                    SecondPlane = placement.SecondPlane,
                    AveragePlane = placement.AveragePlane,
                    MovedAveragePlane = placement.MovedAveragePlane,
                    BasePlane = Offcut.ComputeBasePlane(placement.AveragePlane, dim.X, dim.Y, dim.Z),
                    PositionIndex = ocBaseIndex
                });

                offcutData.RemoveAt(index);

                if (end)
                    break;

                secondPlanes.Add(placement.SecondPlane);
                crv.ClosestPoint(placement.SecondPlane.Origin, out tIntersect);
                distance = placement.SecondPlane.Origin.DistanceTo(crv.PointAtEnd);
            }

            return placed;
        }


        // Position of the shortest Offcut that still spans gap, falling back to fallback when
        // none does. Only called when the guard has already found one that fits.
        private static int ClosingIndex(List<Offcut> offcutData, double gap, int fallback)
        {
            int best = fallback;

            for (int i = 0; i < offcutData.Count; i++)
                if (offcutData[i].Z >= gap && offcutData[i].Z < offcutData[best].Z)
                    best = i;

            return best;
        }
    }
}
