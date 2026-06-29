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
        private const float GeometryEpsilon = 0.5f;
        private const float DegenerateEpsilon = 0.0001f;

        private static bool TryCreatePlane(Vector3 p1, Vector3 p2, Vector3 p3, out Plane plane)
        {
            plane = null!;

            var ab = p2 - p1;
            var ac = p3 - p1;
            if (Vector3.Cross(ac, ab).LengthSquared() < DegenerateEpsilon)
            {
                return false;
            }

            plane = new Plane(p1, p2, p3);
            return !float.IsNaN(plane.Normal.X)
                   && !float.IsNaN(plane.Normal.Y)
                   && !float.IsNaN(plane.Normal.Z)
                   && plane.Normal.LengthSquared() > DegenerateEpsilon;
        }

        public static bool ValidateGeometry(List<Vector3> capA, List<Vector3> capB, out string reason)
        {
            reason = "";
            if (capA == null || capB == null || capA.Count < 3 || capB.Count < 3)
            {
                reason = "Caps must have at least 3 vertices.";
                return false;
            }

            // Cap A Planarity check
            if (!TryCreatePlane(capA[0], capA[1], capA[2], out var planeA))
            {
                reason = "Face 1 (Blue) is degenerate.";
                return false;
            }
            for (int i = 3; i < capA.Count; i++)
            {
                float dist = Math.Abs(Vector3.Dot(planeA.Normal, capA[i]) - planeA.DistanceFromOrigin);
                if (dist > GeometryEpsilon)
                {
                    reason = $"Face 1 (Blue) has non-planar vertex {i} (deviation: {dist:F2}).";
                    return false;
                }
            }

            // Cap B Planarity check
            if (!TryCreatePlane(capB[0], capB[1], capB[2], out var planeB))
            {
                reason = "Face 2 (Green) is degenerate.";
                return false;
            }
            for (int i = 3; i < capB.Count; i++)
            {
                float dist = Math.Abs(Vector3.Dot(planeB.Normal, capB[i]) - planeB.DistanceFromOrigin);
                if (dist > GeometryEpsilon)
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
                if (!TryCreatePlane(p1, p2, p3, out var sidePlane))
                {
                    reason = $"Side face {i} is degenerate.";
                    return false;
                }
                float dist = Math.Abs(Vector3.Dot(sidePlane.Normal, p4) - sidePlane.DistanceFromOrigin);
                if (dist > GeometryEpsilon)
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
                if (!TryCreatePlane(capA[i], capB[i], capB[(i + 1) % n], out var sidePlane))
                {
                    reason = $"Side face {i} is degenerate.";
                    return false;
                }
                faces.Add(sidePlane);
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
                    if (dist > GeometryEpsilon)
                    {
                        reason = "Geometry is non-convex (some vertices lie outside).";
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool ValidateGeneratedFaces(IReadOnlyList<IReadOnlyList<Vector3>> faces, out string reason)
        {
            reason = "";
            if (faces == null || faces.Count < 4)
            {
                reason = "Generated brush must have at least 4 faces.";
                return false;
            }

            var planes = new List<Plane>();
            var allVerts = new List<Vector3>();
            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                var face = faces[faceIndex];
                if (face.Count < 3)
                {
                    reason = $"Generated face {faceIndex} has fewer than 3 vertices.";
                    return false;
                }

                if (!TryCreatePlane(face[0], face[1], face[2], out var plane))
                {
                    reason = $"Generated face {faceIndex} is degenerate.";
                    return false;
                }

                for (int i = 3; i < face.Count; i++)
                {
                    float dist = Math.Abs(Vector3.Dot(plane.Normal, face[i]) - plane.DistanceFromOrigin);
                    if (dist > GeometryEpsilon)
                    {
                        reason = $"Generated face {faceIndex} is non-planar (deviation: {dist:F2}).";
                        return false;
                    }
                }

                const float MinEdgeLength = 0.25f;
                for (int i = 0; i < face.Count; i++)
                {
                    var p1 = face[i];
                    var p2 = face[(i + 1) % face.Count];
                    if (Vector3.Distance(p1, p2) < MinEdgeLength)
                    {
                        reason = $"Generated face {faceIndex} has an extremely short edge (length {Vector3.Distance(p1, p2):F4} < {MinEdgeLength}). GoldSrc compilers might collapse these vertices and report degenerate solid errors.";
                        return false;
                    }
                }

                planes.Add(plane);
                allVerts.AddRange(face);
            }

            if (allVerts.Count == 0)
            {
                reason = "Generated brush has no vertices.";
                return false;
            }

            Vector3 centroid = allVerts.Aggregate(Vector3.Zero, (a, b) => a + b) / allVerts.Count;
            foreach (var plane in planes)
            {
                var oriented = plane;
                float eval = Vector3.Dot(oriented.Normal, centroid) - oriented.DistanceFromOrigin;
                if (eval > 0)
                {
                    oriented = new Plane(-oriented.Normal, -oriented.DistanceFromOrigin);
                }

                foreach (var vert in allVerts)
                {
                    float dist = Vector3.Dot(oriented.Normal, vert) - oriented.DistanceFromOrigin;
                    if (dist > GeometryEpsilon)
                    {
                        reason = "Generated brush is non-convex (some vertices lie outside).";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
