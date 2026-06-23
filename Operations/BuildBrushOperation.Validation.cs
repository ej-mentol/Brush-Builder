using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.DataStructures.Geometric;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace HammerTime.BrushBuilder.Operations
{
    public static partial class BuildBrushOperation
    {
        public static bool ValidateGeometry(List<Vector3> capA, List<Vector3> capB, out string reason)
        {
            reason = "";
            if (capA == null || capB == null || capA.Count < 3 || capB.Count < 3)
            {
                reason = "Caps must have at least 3 vertices.";
                return false;
            }

            // Cap A Planarity check
            var planeA = new Plane(capA[0], capA[1], capA[2]);
            for (int i = 3; i < capA.Count; i++)
            {
                float dist = Math.Abs(Vector3.Dot(planeA.Normal, capA[i]) - planeA.DistanceFromOrigin);
                if (dist > 0.5f)
                {
                    reason = $"Face 1 (Blue) has non-planar vertex {i} (deviation: {dist:F2}).";
                    return false;
                }
            }

            // Cap B Planarity check
            var planeB = new Plane(capB[0], capB[1], capB[2]);
            for (int i = 3; i < capB.Count; i++)
            {
                float dist = Math.Abs(Vector3.Dot(planeB.Normal, capB[i]) - planeB.DistanceFromOrigin);
                if (dist > 0.5f)
                {
                    reason = $"Face 2 (Green) has non-planar vertex {i} (deviation: {dist:F2}).";
                    return false;
                }
            }

            // Side faces Planarity check
            int n = capA.Count;
            for (int i = 0; i < n; i++)
            {
                var p1 = capA[i];
                var p2 = capB[i];
                var p3 = capB[(i + 1) % n];
                var p4 = capA[(i + 1) % n];
                var sidePlane = new Plane(p1, p2, p3);
                float dist = Math.Abs(Vector3.Dot(sidePlane.Normal, p4) - sidePlane.DistanceFromOrigin);
                if (dist > 0.5f)
                {
                    reason = $"Side face {i} is non-planar (deviation: {dist:F2}).";
                    return false;
                }
            }

            // Convexity Check: centroid must be behind or on the plane of all faces
            var allVerts = capA.Concat(capB).ToList();
            var faces = new List<Plane> { planeA, planeB };
            for (int i = 0; i < n; i++)
            {
                faces.Add(new Plane(capA[i], capB[i], capB[(i + 1) % n]));
            }

            Vector3 centroid = allVerts.Aggregate(Vector3.Zero, (a, b) => a + b) / allVerts.Count;
            foreach (var face in faces)
            {
                Plane oriented = face;
                float eval = Vector3.Dot(oriented.Normal, centroid) - oriented.DistanceFromOrigin;
                if (eval > 0)
                {
                    oriented = new Plane(-oriented.Normal, -oriented.DistanceFromOrigin);
                }

                foreach (var vert in allVerts)
                {
                    float dist = Vector3.Dot(oriented.Normal, vert) - oriented.DistanceFromOrigin;
                    if (dist > 0.5f)
                    {
                        reason = "Geometry is non-convex (some vertices lie outside).";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
