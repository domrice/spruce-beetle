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


using System.Collections.Generic;
using Rhino.Geometry;


namespace SpruceBeetle.Alignment
{
    // The joint pass shared by the Tenon Joints and Spline Joints components: walk the aligned
    // Offcuts, ask a cutter for the Breps to subtract at each end, and subtract them. The joint
    // types differ only in that cutter geometry.
    public static class JointPass
    {
        // Cutter geometry for one end of one Offcut. cutters are subtracted from the Offcut;
        // display is what the component reports as the joint - the two differ where the cutter
        // has to overshoot the Offcut to cut cleanly.
        public delegate void JointCutter(Plane plane, double[] minValue, Offcut offcut, out Brep[] cutters, out Brep[] display);

        public static void Run(List<Offcut> alignedOffcuts, int jointCount, JointCutter cut,
            out List<Offcut_GH> offcuts, out Brep[,] joints, out double[] jointVolumes)
        {
            int count = alignedOffcuts.Count;

            Brep[] cutOffcuts = new Brep[count];
            double[] offcutVolumes = new double[count];

            Brep[,] display = new Brep[count, jointCount];
            double[] volumes = new double[count];

            // ponytail: Brep.CreateBooleanDifference inside Joint.CutOffcut is not documented as
            // thread-safe. Kept parallel because that is what this code already did. Upgrade path
            // if it turns out not to be: drop this to a plain for loop - it is a one-word change.
            System.Threading.Tasks.Parallel.For(0, count, i =>
            {
                Offcut offcut = alignedOffcuts[i];
                List<double[]> minimumDimensions = Utility.GetMinimumDimension(alignedOffcuts, i);

                List<Brep> cutters = new List<Brep>();

                // every Offcut but the first is jointed at its start plane
                if (i > 0)
                {
                    cut(offcut.FirstPlane, minimumDimensions[0], offcut, out Brep[] firstCutters, out Brep[] _);
                    cutters.AddRange(firstCutters);
                }

                // and every Offcut but the last at its end plane. Only the end joint is reported,
                // so each joint between two Offcuts is counted exactly once.
                if (i < count - 1)
                {
                    cut(offcut.SecondPlane, minimumDimensions[1], offcut, out Brep[] secondCutters, out Brep[] secondDisplay);
                    cutters.AddRange(secondCutters);

                    for (int j = 0; j < secondDisplay.Length; j++)
                    {
                        display[i, j] = secondDisplay[j];
                        volumes[i] += secondDisplay[j].GetVolume(0.0001, 0.0001);
                    }
                }

                // a lone Offcut has no neighbour to joint to
                cutOffcuts[i] = cutters.Count == 0 ? offcut.OffcutGeometry : Joint.CutOffcut(cutters.ToArray(), offcut.OffcutGeometry);
                offcutVolumes[i] = cutOffcuts[i].GetVolume(0.0001, 0.0001);
            });

            offcuts = new List<Offcut_GH>();

            for (int i = 0; i < count; i++)
                offcuts.Add(new Offcut_GH(new Offcut(alignedOffcuts[i])
                {
                    OffcutGeometry = cutOffcuts[i],
                    FabVol = offcutVolumes[i]
                }));

            joints = display;
            jointVolumes = volumes;
        }
    }
}
