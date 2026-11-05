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
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;


namespace SpruceBeetle.Alignment
{
    public class IntersectionJoints_GH : GH_Component
    {
        public IntersectionJoints_GH()
          : base("Intersection Joints", "IntJoints", "Create joints at the intersections of the alignments of Offcuts", "Spruce Beetle", "    Alignment")
        {
        }


        // parameter inputs
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("First Alignment", "FA", "First list of aligned Offcuts", GH_ParamAccess.list);
            pManager.AddGenericParameter("Second Alignment", "SA", "Second list of aligned Offcuts", GH_ParamAccess.list);
            pManager.AddPointParameter("Intersection Point", "IP", "Intersection point between the two alignments", GH_ParamAccess.item);
            pManager.AddNumberParameter("Rotate Joint", "RJ", "Rotate the joint to alter its direction", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Width", "W", "The width of the lap joint", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Joint Type", "JT", "Adds the specified joint type: 0 = pass-through joint, 1 = cross-lap joint", GH_ParamAccess.item, 1);

            for (int i = 0; i < pManager.ParamCount; i++)
                pManager[i].WireDisplay = GH_ParamWireDisplay.faint;
        }


        // parameter outputs
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Offcuts", "Oc", "List of aligned and intersected Offcuts", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Intersection Offcuts", "IOc", "List of intersecting Offcuts", GH_ParamAccess.tree);

            for (int i = 0; i < pManager.ParamCount; i++)
                pManager[i].WireDisplay = GH_ParamWireDisplay.faint;
        }


        // main
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // variables to reference the input parameters to
            List<Offcut> firstOffcuts = new List<Offcut>();
            List<Offcut> secondOffcuts = new List<Offcut>();
            Point3d intPt = new Point3d();
            double angle = 0.0;
            double width = 2.0;
            int jointType = 0;

            // access input parameters
            if (!DA.GetDataList(0, firstOffcuts)) return;
            if (!DA.GetDataList(1, secondOffcuts)) return;
            if (!DA.GetData(2, ref intPt)) return;
            if (!DA.GetData(3, ref angle)) return;
            if (!DA.GetData(4, ref width)) return;
            if (!DA.GetData(5, ref jointType)) return;

            // initialise data trees and paths
            DataTree<Offcut_GH> offcutGHList = new DataTree<Offcut_GH>();
            DataTree<Offcut_GH> intOffcutGHList = new DataTree<Offcut_GH>();
            GH_Path firstPath = new GH_Path(0);
            GH_Path secondPath = new GH_Path(1);

            switch (jointType)
            {
                // cross-lap joint
                case 1:
                    {
                        // call GetCuttingPlanes method
                        GetCuttingPlanes(firstOffcuts, intPt, angle, out List<int> firstIndices, out Offcut firstOffcut, out Plane firstPlane);
                        GetCuttingPlanes(secondOffcuts, intPt, angle, out List<int> secondIndices, out Offcut secondOffcut, out Plane secondPlane);

                        // rotate first plane
                        firstPlane.Transform(Transform.Rotation(Utility.ConvertToRadians(180), firstPlane.ZAxis, firstPlane.Origin));

                        // create average plane out of the first and second one
                        Point3d originPt = new Point3d((firstPlane.Origin + secondPlane.Origin) / 2);
                        Plane averagePlane = new Plane(originPt, (firstPlane.XAxis + secondPlane.XAxis) / 2, (firstPlane.YAxis + secondPlane.YAxis) / 2);
                        averagePlane.Origin = intPt;

                        // call GetCuttingGeometry method
                        GetCuttingGeometry(averagePlane, firstPlane, firstOffcut, 1, width, out Brep firstBrep);
                        GetCuttingGeometry(averagePlane, secondPlane, secondOffcut, -1, width, out Brep secondBrep);

                        // call CreateIntersectionJoint method
                        CreateIntersectionJoint(firstBrep, firstOffcuts, firstIndices, out List<Offcut> firstOffcutList, out List<Offcut> firstIntOffcuts);
                        CreateIntersectionJoint(secondBrep, secondOffcuts, secondIndices, out List<Offcut> secondOffcutList, out List<Offcut> secondIntOffcuts);

                        // add all Offcuts to the respective data tree
                        for (int i = 0; i < firstOffcutList.Count; i++)
                            offcutGHList.Add(new Offcut_GH(firstOffcutList[i]), firstPath);

                        for (int i = 0; i < secondOffcutList.Count; i++)
                            offcutGHList.Add(new Offcut_GH(secondOffcutList[i]), secondPath);

                        // the two lists are the same length unless a cut failed, so walk them apart
                        for (int i = 0; i < firstIntOffcuts.Count; i++)
                            intOffcutGHList.Add(new Offcut_GH(firstIntOffcuts[i]), firstPath);

                        for (int i = 0; i < secondIntOffcuts.Count; i++)
                            intOffcutGHList.Add(new Offcut_GH(secondIntOffcuts[i]), secondPath);
                    }
                    break;

                // pass-through joint: the second alignment is cut by the geometry of the first
                default:
                    {
                        // call GetCuttingPlanes method
                        GetCuttingPlanes(firstOffcuts, intPt, angle, out _, out Offcut firstOffcut, out _);
                        GetCuttingPlanes(secondOffcuts, intPt, angle, out List<int> secondIndices, out _, out _);

                        // create Cutter geometry through scaling the closest brep
                        Brep offcutCutter = firstOffcut.OffcutGeometry.DuplicateBrep();
                        offcutCutter.Transform(Transform.Scale(firstOffcut.AveragePlane, 2, width, 4));

                        // call CreateIntersectionJoint method
                        CreateIntersectionJoint(offcutCutter, secondOffcuts, secondIndices, out List<Offcut> secondOffcutList, out List<Offcut> secondIntOffcuts);

                        // add all Offcuts to the respective data tree
                        for (int i = 0; i < firstOffcuts.Count; i++)
                            offcutGHList.Add(new Offcut_GH(firstOffcuts[i]), firstPath);

                        for (int i = 0; i < secondOffcutList.Count; i++)
                            offcutGHList.Add(new Offcut_GH(secondOffcutList[i]), secondPath);

                        for (int i = 0; i < secondIntOffcuts.Count; i++)
                            intOffcutGHList.Add(new Offcut_GH(secondIntOffcuts[i]), secondPath);
                    }
                    break;
            }

            // access output parameters
            DA.SetDataTree(0, offcutGHList);
            DA.SetDataTree(1, intOffcutGHList);
        }


        //------------------------------------------------------------
        // CreateIntersectionJoint method
        //------------------------------------------------------------
        protected void CreateIntersectionJoint(Brep cutter, List<Offcut> offcutList, List<int> indices, out List<Offcut> outOffcutList, out List<Offcut> intersectOffcut)
        {
            List<Brep> cutters = new List<Brep> { cutter };

            outOffcutList = new List<Offcut>();
            intersectOffcut = new List<Offcut>();

            for (int i = 0; i < offcutList.Count; i++)
            {
                Offcut newOffcut = new Offcut(offcutList[i]);

                if (indices.Contains(i))
                {
                    // cut every Offcut on its own: cutting them as one list and matching the pieces
                    // back by sorted volume pairs each piece with its Offcut only by coincidence
                    Brep[] pieces = Brep.CreateBooleanDifference(new List<Brep> { newOffcut.OffcutGeometry }, cutters, 0.0001);

                    if (pieces == null || pieces.Length == 0)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not cut the joint into Offcut {i}; it is returned uncut.");
                    else
                    {
                        foreach (Brep piece in pieces)
                        {
                            // brep clean-up
                            piece.Faces.SplitKinkyFaces(0.0001);
                            piece.MergeCoplanarFaces(0.0001);
                            piece.Faces.SplitKinkyFaces(0.0001);

                            // check solid orientation
                            if (BrepSolidOrientation.Inward == piece.SolidOrientation)
                                piece.Flip();
                        }

                        // the cutter can sever the Offcut, so keep the biggest piece
                        newOffcut.OffcutGeometry = pieces.OrderByDescending(brep => brep.GetVolume(0.0001, 0.0001)).First();
                        intersectOffcut.Add(newOffcut);
                    }
                }

                outOffcutList.Add(newOffcut);
            }
        }


        //------------------------------------------------------------
        // GetCuttingGeometry method
        //------------------------------------------------------------
        protected void GetCuttingGeometry(Plane averagePlane, Plane originalPlane, Offcut offcut, int dir, double width, out Brep cutterBrep)
        {
            Plane average = new Plane(averagePlane);
            double angle = Vector3d.VectorAngle(averagePlane.ZAxis, originalPlane.ZAxis);
            average.Transform(Transform.Rotation(dir * angle, averagePlane.XAxis, averagePlane.Origin));

            // create cutter breps
            cutterBrep = Offcut.CreateOffcutBrep(offcut, average, offcut.PositionIndex);

            // move cutter
            cutterBrep.Transform(Transform.Translation(dir * average.XAxis * offcut.X / 2));

            // scale cutter
            cutterBrep.Transform(Transform.Scale(average, 3, width, 3));
        }


        //------------------------------------------------------------
        // GetCuttingPlanes method
        //------------------------------------------------------------
        protected void GetCuttingPlanes(List<Offcut> offcutList, Point3d intPt, double angle, out List<int> indices, out Offcut offcut, out Plane basePlane)
        {
            // call GetClosestOffcutIndex method
            GetClosestOffcutIndex(offcutList, intPt, out int index, out indices);

            // get the Offcut closest to the intersection point
            offcut = new Offcut(offcutList[index]);

            // get base plane
            basePlane = new Plane(offcutList[index].MovedAveragePlane);

            // rotate base plane as indicated
            basePlane.Transform(Transform.Rotation(Utility.ConvertToRadians(angle), basePlane.ZAxis, basePlane.Origin));
            //basePlane.Transform(Transform.Rotation(Utility.ConvertToRadians(secAngle), basePlane.XAxis, basePlane.Origin));
        }


        //------------------------------------------------------------
        // GetClosestOffcutIndex method
        //------------------------------------------------------------
        protected void GetClosestOffcutIndex(List<Offcut> offcutList, Point3d intPt, out int closestIndex, out List<int> indexList)
        {
            // initialise list to store the distances
            List<double> distances = new List<double>();

            // calculate the distances from the intersection point to a closest point of each Offcut
            foreach (Offcut offcut in offcutList)
            {
                //Point3d closestPt = offcut.OffcutGeometry.ClosestPoint(intPt);
                double distance = intPt.DistanceTo(offcut.AveragePlane.Origin);
                distances.Add(distance);
            }

            // the three Offcuts closest to the intersection point, nearest first
            indexList = Utility.SortedIndices(distances, 3);
            closestIndex = indexList[0];
        }


        //------------------------------------------------------------
        // Else
        //------------------------------------------------------------

        // exposure property
        public override GH_Exposure Exposure => GH_Exposure.secondary;

        // add icon
        protected override System.Drawing.Bitmap Icon => Utility.Icon("24x24_IntersectionJoints");

        // component giud
        public override Guid ComponentGuid => new Guid("7B5A7106-2285-4056-9117-BC95A627FD04");
    }
}