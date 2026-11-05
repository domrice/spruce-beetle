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
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace SpruceBeetle.Fabricate
{
    public class GetCoordinates_GH : GH_Component
    {
        public GetCoordinates_GH() : base("Get Coordinates", "Coords", "Convert the data from an CSV file to point coordinates", "Spruce Beetle", "    Fabricate")
        {
        }


        // parameter inputs
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "file", "Provide a path to a CSV file", GH_ParamAccess.item);
            pManager.AddTextParameter("Delimiter", "D", "Provide a delimiter", GH_ParamAccess.item, ";");

            pManager[0].WireDisplay = GH_ParamWireDisplay.faint;
        }


        // parameter outputs
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Coordinates", "C", "The target coordinates as points", GH_ParamAccess.list);

            pManager[0].WireDisplay = GH_ParamWireDisplay.faint;
        }


        // main
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            string delimiter = ";";

            DA.GetData(0, ref filePath);
            DA.GetData(1, ref delimiter);

            if (filePath == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No file path provided!");
                return;
            }

            if (!System.IO.File.Exists(filePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"No file at \"{filePath}\"");
                return;
            }

            // one row per point: x, y, z
            List<Point3d> ptList = new List<Point3d>();
            List<int> badRows = new List<int>();

            List<string[]> rows = CSV.ReadRows(filePath, delimiter);

            for (int i = 0; i < rows.Count; i++)
            {
                if (CSV.TryParseRow(rows[i], 3, out double[] values))
                    ptList.Add(new Point3d(values[0], values[1], values[2]));
                else
                    badRows.Add(i + 1);
            }

            if (badRows.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Skipped {badRows.Count} row(s) that are not three numbers separated by \"{delimiter}\": line(s) {string.Join(", ", badRows)}");

            DA.SetDataList(0, ptList);
        }


        //------------------------------------------------------------
        // Else
        //------------------------------------------------------------

        // exposure property
        public override GH_Exposure Exposure => GH_Exposure.primary;

        // add icon
        protected override System.Drawing.Bitmap Icon => Utility.Icon("24x24_CSVCoord");

        // component giud
        public override Guid ComponentGuid => new Guid("4D21D990-C212-4500-9B04-9DB6C9BB9BD8");
    }
}