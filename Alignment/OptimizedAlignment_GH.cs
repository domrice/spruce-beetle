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
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;


namespace SpruceBeetle.Alignment
{
    public class OptimizedAlignment_GH : GH_Component
    {
        public OptimizedAlignment_GH()
          : base("Optimized Alignment", "OptiAlign", "Align a list of Offcuts along a given curve", "Spruce Beetle", "    Alignment")
        {
        }


        // value list
        GH_ValueList valueList = null;
        IGH_Param parameter = null;


        // parameter inputs
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to align Offcuts to, in priority order: the first curve gets first pick of the Offcuts", GH_ParamAccess.list);
            pManager.AddGenericParameter("Offcut Data", "OcD", "List of dimensions of all the Offcuts", GH_ParamAccess.list);
            pManager.AddTextParameter("Offcut Position", "OcP", "Position of the Offcuts", GH_ParamAccess.item, "mid-mid");
            pManager.AddNumberParameter("Start Angle", "SA", "Rotate the start of the alignement by the provided angle", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("End Angle", "EA", "Rotate the end of the alignment by the provided angle", GH_ParamAccess.item, 0);

            parameter = pManager[2];

            for (int i = 0; i < pManager.ParamCount; i++)
                pManager[i].WireDisplay = GH_ParamWireDisplay.faint;
        }


        // parameter outputs
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Aligned Offcuts", "AOc", "Aligned Offcuts, one branch per curve", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Unused Offcuts", "UOc", "Offcuts no curve used, to be used on another alignment", GH_ParamAccess.list);
            pManager.AddCurveParameter("Centroid Curves", "CC", "The curve at the center of the aligned Offcuts, one per input curve", GH_ParamAccess.list);
            pManager.AddNumberParameter("Trim Waste", "TW", "Volume lost trimming the Offcuts to their end planes, one value per curve", GH_ParamAccess.list);

            pManager.HideParameter(2);

            for (int i = 0; i < pManager.ParamCount; i++)
                pManager[i].WireDisplay = GH_ParamWireDisplay.faint;
        }


        // create value list
        protected override void BeforeSolveInstance()
        {
            if (valueList == null)
            {
                if (parameter.Sources.Count == 0)
                    valueList = new GH_ValueList();
                else
                {
                    foreach (var source in parameter.Sources)
                    {
                        if (source is GH_ValueList)
                            valueList = source as GH_ValueList;

                        return;
                    }
                }

                valueList.CreateAttributes();
                valueList.Attributes.Pivot = new System.Drawing.PointF(this.Attributes.Pivot.X - 200, this.Attributes.Pivot.Y - 0);
                valueList.ListItems.Clear();

                List<string> baseOrientation = Offcut.BasePosition();

                foreach (string param in baseOrientation)
                    valueList.ListItems.Add(new GH_ValueListItem(param, $"\"{param}\""));

                Instances.ActiveCanvas.Document.AddObject(valueList, false);
                parameter.AddSource(valueList);
                parameter.CollectData();
            }
        }


        // main
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // variables to reference the input parameters to
            List<Curve> curves = new List<Curve>();
            List<Offcut> offcutData = new List<Offcut>();
            string ocBaseType = "";
            double rotateStart = 0;
            double rotateEnd = 0;

            // access input parameters
            if (!DA.GetDataList(0, curves)) return;
            if (!DA.GetDataList(1, offcutData)) return;
            if (!DA.GetData(2, ref ocBaseType)) return;
            if (!DA.GetData(3, ref rotateStart)) return;
            if (!DA.GetData(4, ref rotateEnd)) return;

            // a linear curve has no curvature to match against, so fall back to longest-first;
            // otherwise the curvature interval is constant per curve, so compute it once
            Func<Curve, AlignmentRun.OffcutPicker> pickerFor = crv =>
            {
                if (crv.IsLinear())
                    return (remaining, t) => { Utility.GetMaxZ(remaining, out _, out int index); return index; };

                Interval curveBounds = Utility.CurveCurvature(crv);
                return (remaining, t) => Utility.CurvatureIndex(crv, curveBounds, remaining, t);
            };

            // run the shared alignment loop once per curve, sharing one Offcut inventory -
            // offcutData is left holding what no curve used
            List<List<Offcut>> aligned = AlignmentRun.RunAll(curves, offcutData, new Interval(rotateStart, rotateEnd),
                Offcut.GetBasePosition()[ocBaseType], pickerFor);

            // a curve with no Offcuts means the inventory ran out before its turn came
            int unaligned = aligned.Count(ocs => ocs.Count == 0);

            if (unaligned > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Ran out of Offcuts: {unaligned} of {curves.Count} curves got none.");

            // an Offcut the curve leaves is too long for the radius it was placed on
            List<int> infeasible = new List<int>();

            for (int i = 0; i < aligned.Count; i++)
                infeasible.AddRange(aligned[i].Where(oc => Utility.CurveExitsOffcut(curves[i], oc)).Select(oc => oc.Index));

            if (infeasible.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"The curve leaves {infeasible.Count} Offcut(s) - too long for the radius they sit on. Index: {string.Join(", ", infeasible)}.");

            // report the trim loss as a share of the material actually placed
            List<double> waste = aligned.Select(Utility.TrimWaste).ToList();
            double placedVol = aligned.SelectMany(ocs => ocs).Sum(oc => oc.Vol);

            Message = placedVol > 0 ? $"trim waste {waste.Sum() / placedVol:P1}" : null;

            // access output parameters
            DataTree<Offcut_GH> alignedTree = new DataTree<Offcut_GH>();

            for (int i = 0; i < aligned.Count; i++)
                alignedTree.AddRange(aligned[i].Select(oc => new Offcut_GH(oc)), new GH_Path(i));

            DA.SetDataTree(0, alignedTree);
            DA.SetDataList(1, offcutData.Select(oc => new Offcut_GH(oc)));
            DA.SetDataList(2, aligned.Select(ocs => ocs.Count > 0 ? Utility.GetOffcutBaseCurve(ocs) : null));
            DA.SetDataList(3, waste);
        }

        //------------------------------------------------------------
        // Else
        //------------------------------------------------------------

        // exposure property
        public override GH_Exposure Exposure => GH_Exposure.primary;

        // add icon
        protected override System.Drawing.Bitmap Icon => Utility.Icon("24x24_OptimizedAlignment");

        // component giud
        public override Guid ComponentGuid => new Guid("7D351E29-9320-4F03-807F-31896403D1CF");
    }
}