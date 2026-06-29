using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.DataStructures.Geometric;
using Face = Sledge.BspEditor.Primitives.MapObjectData.Face;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace HammerTime.BrushBuilder.Operations
{
    public static class BrushBuilderColors
    {
        // Viewport face overlays
        public static System.Drawing.Color Face1 { get; set; } = System.Drawing.Color.DeepSkyBlue;
        public static System.Drawing.Color Face2 { get; set; } = System.Drawing.Color.LimeGreen;
        public static System.Drawing.Color FaceClip { get; set; } = System.Drawing.Color.Coral;
        public static System.Drawing.Color FaceHover { get; set; } = System.Drawing.Color.Gold;

        // UI active states
        public static System.Drawing.Color ButtonActive { get; set; } = System.Drawing.Color.DodgerBlue;
        public static System.Drawing.Color ButtonActiveFore { get; set; } = System.Drawing.Color.White;
        public static System.Drawing.Color ButtonSwapActive { get; set; } = System.Drawing.Color.Orange;
        public static System.Drawing.Color ButtonSwapActiveFore { get; set; } = System.Drawing.Color.Black;

        // Validation hints
        public static System.Drawing.Color ValidationWarn { get; set; } = System.Drawing.Color.OrangeRed;

        // Цвет превью браша (прозрачный голубой с альфой 64 для лучшего обзора геометрии)
        public static System.Drawing.Color PreviewColor { get; set; } = System.Drawing.Color.FromArgb(64, System.Drawing.Color.DodgerBlue);
    }

    public static partial class BuildBrushOperation
    {
        public static Action<string>? OnLog { get; set; }

        public sealed class GeneratedFaceGeometry
        {
            public GeneratedFaceGeometry(Face sourceFace, List<Vector3> vertices)
            {
                SourceFace = sourceFace;
                Vertices = vertices;
            }

            public Face SourceFace { get; }
            public List<Vector3> Vertices { get; }
        }

        public static bool TryCreateGeneratedFaces(
            Face faceA,
            IReadOnlyList<Face> otherFaces,
            string sizeMode,
            string alignment,
            string depth,
            float offsetA, float offsetB,
            bool usePercentageOffsetA, bool usePercentageOffsetB,
            float thickness, bool usePercentageThick,
            string copySide,
            int alignmentShiftOffset,
            bool triangulate,
            string splitMode,
            int slices,
            out List<List<GeneratedFaceGeometry>> generatedSolids,
            out string reason,
            bool enableLogging = false,
            IReadOnlyList<Vector3>? otherHitPoints = null
        )
        {
            generatedSolids = new List<List<GeneratedFaceGeometry>>();
            reason = "";

            if (otherFaces == null || otherFaces.Count == 0)
            {
                reason = "At least 2 faces must be selected.";
                return false;
            }

            var faceB = otherFaces[0];
            var caps = GetCaps(
                faceA, faceB,
                sizeMode,
                alignment,
                depth,
                offsetA, offsetB,
                usePercentageOffsetA, usePercentageOffsetB,
                thickness, usePercentageThick,
                copySide,
                alignmentShiftOffset,
                enableLogging
            );

            if (caps == null)
            {
                reason = "Could not compute cap geometry.";
                return false;
            }

            return TryCreateGeneratedFacesFromCaps(faceA, faceB, otherFaces, otherHitPoints, caps.Value.CapA, caps.Value.CapB, triangulate, splitMode, slices, generatedSolids, out reason, enableLogging);
        }

        private static bool TryCreateGeneratedFacesFromCaps(
            Face faceA,
            Face faceB,
            IReadOnlyList<Face> otherFaces,
            IReadOnlyList<Vector3>? otherHitPoints,
            List<Vector3> capA,
            List<Vector3> capB,
            bool triangulate,
            string splitMode,
            int slices,
            List<List<GeneratedFaceGeometry>> generatedSolids,
            out string reason,
            bool enableLogging
        )
        {
            reason = "";
            generatedSolids.Clear();

            if (capA.Count < 3 || capB.Count < 3 || capA.Count != capB.Count)
            {
                reason = "Generated caps are incomplete.";
                return false;
            }

            int n = capA.Count;
            int slicesCount = Math.Max(1, slices);

            Vector3 centroidCapA = capA.Aggregate(Vector3.Zero, (a, b) => a + b) / capA.Count;
            Vector3 centroidCapB = capB.Aggregate(Vector3.Zero, (a, b) => a + b) / capB.Count;
            float totalSpan = (centroidCapB - centroidCapA).Length();
            float sliceThickness = totalSpan / slicesCount;
            if (sliceThickness < 1.0f && slicesCount > 1)
            {
                reason = $"Slice thickness is too small ({sliceThickness:F2} units). Minimum allowed is 1.0 unit. Decrease Slices or increase distance.";
                return false;
            }

            for (int sVal = 0; sVal < slicesCount; sVal++)
            {
                float tStart = sVal / (float)slicesCount;
                float tEnd = (sVal + 1) / (float)slicesCount;

                var sliceCapA = new List<Vector3>();
                var sliceCapB = new List<Vector3>();
                for (int i = 0; i < n; i++)
                {
                    sliceCapA.Add(capA[i] + (capB[i] - capA[i]) * tStart);
                    sliceCapB.Add(capA[i] + (capB[i] - capA[i]) * tEnd);
                }

                Vector3 solidCentroid = sliceCapA.Concat(sliceCapB).Aggregate(Vector3.Zero, (a, b) => a + b) / (sliceCapA.Count + sliceCapB.Count);            
                Vector3 referenceCentroid = solidCentroid;

                bool OrientOutwardForCentroid(List<Vector3> verts, Vector3 bodyCentroid)
                {
                    if (verts.Count < 3) return false;
                    if (!TryCreatePlane(verts[0], verts[1], verts[2], out var plane))
                    {
                        return false;
                    }
                    var faceCentroid = verts.Aggregate(Vector3.Zero, (a, b) => a + b) / verts.Count;
                    if (Vector3.Dot(plane.Normal, faceCentroid - bodyCentroid) < 0)
                    {
                        verts.Reverse();
                    }
                    return true;
                }

                bool OrientOutward(List<Vector3> verts, out List<Vector3> oriented, out string orientReason)
                {
                    oriented = verts;
                    orientReason = "";

                    if (!TryCreatePlane(verts[0], verts[1], verts[2], out var plane))
                    {
                        orientReason = "Generated face is degenerate.";
                        return false;
                    }

                    var faceCentroid = verts.Aggregate(Vector3.Zero, (a, b) => a + b) / verts.Count;
                    if (Vector3.Dot(plane.Normal, faceCentroid - referenceCentroid) < 0)
                    {
                        oriented = new List<Vector3>(verts);
                        oriented.Reverse();
                    }
                    return true;
                }

                if (otherFaces.Count <= 1)
                {
                    if (triangulate)
                    {
                        if (splitMode.StartsWith("One Solid", StringComparison.OrdinalIgnoreCase))
                        {
                            var oneSolidFaces = new List<GeneratedFaceGeometry>();

                            var capAOriented = sliceCapA.ToList();
                            if (OrientOutwardForCentroid(capAOriented, solidCentroid))
                            {
                                oneSolidFaces.Add(new GeneratedFaceGeometry(faceA, capAOriented));
                            }

                            var capBOriented = sliceCapB.ToList();
                            if (OrientOutwardForCentroid(capBOriented, solidCentroid))
                            {
                                oneSolidFaces.Add(new GeneratedFaceGeometry(faceB, capBOriented));
                            }

                            for (int i = 0; i < n; i++)
                            {
                                var p1 = sliceCapA[i];
                                var p2 = sliceCapB[i];
                                var p3 = sliceCapB[(i + 1) % n];
                                var p4 = sliceCapA[(i + 1) % n];

                                var t1a = new List<Vector3> { p1, p4, p2 };
                                var t1b = new List<Vector3> { p2, p3, p4 };

                                OrientOutwardForCentroid(t1a, solidCentroid);
                                OrientOutwardForCentroid(t1b, solidCentroid);

                                bool isT1aValid = TryCreatePlane(t1a[0], t1a[1], t1a[2], out var plane1a);
                                bool isT1bValid = TryCreatePlane(t1b[0], t1b[1], t1b[2], out var plane1b);

                                bool diagonal1IsConvex = true;
                                if (isT1aValid)
                                {
                                    float dist3 = Vector3.Dot(plane1a.Normal, p3) - plane1a.DistanceFromOrigin;
                                    if (dist3 > 1e-4f) diagonal1IsConvex = false;
                                }
                                if (isT1bValid)
                                {
                                    float dist1 = Vector3.Dot(plane1b.Normal, p1) - plane1b.DistanceFromOrigin;
                                    if (dist1 > 1e-4f) diagonal1IsConvex = false;
                                }

                                bool forceDiag1 = splitMode.Contains("Diag \\");
                                bool forceDiag2 = splitMode.Contains("Diag /");

                                if ((diagonal1IsConvex && !forceDiag2) || forceDiag1)
                                {
                                    oneSolidFaces.Add(new GeneratedFaceGeometry(faceA, t1a));
                                    oneSolidFaces.Add(new GeneratedFaceGeometry(faceA, t1b));
                                }
                                else
                                {
                                    var t2a = new List<Vector3> { p1, p3, p2 };
                                    var t2b = new List<Vector3> { p1, p4, p3 };

                                    OrientOutwardForCentroid(t2a, solidCentroid);
                                    OrientOutwardForCentroid(t2b, solidCentroid);

                                    oneSolidFaces.Add(new GeneratedFaceGeometry(faceA, t2a));
                                    oneSolidFaces.Add(new GeneratedFaceGeometry(faceA, t2b));
                                }
                            }

                            generatedSolids.Add(oneSolidFaces);
                            continue;
                        }

                        Vector3 centroidA = sliceCapA.Aggregate(Vector3.Zero, (a, b) => a + b) / sliceCapA.Count;
                        Vector3 centroidB = sliceCapB.Aggregate(Vector3.Zero, (a, b) => a + b) / sliceCapB.Count;

                        for (int i = 0; i < n; i++)
                        {
                            var p1 = sliceCapA[i];
                            var p2 = sliceCapB[i];
                            var p3 = sliceCapB[(i + 1) % n];
                            var p4 = sliceCapA[(i + 1) % n];

                            bool isPlanar = true;
                            if (TryCreatePlane(p1, p2, p3, out var sidePlane))
                            {
                                float dist = Math.Abs(Vector3.Dot(sidePlane.Normal, p4) - sidePlane.DistanceFromOrigin);
                                if (dist > GeometryEpsilon) isPlanar = false;
                            }
                            else
                            {
                                isPlanar = false;
                            }

                            if (splitMode == "Tetrahedral" && !isPlanar)
                            {
                                var t1 = new List<List<Vector3>>
                                {
                                    new List<Vector3> { p1, p4, centroidA },
                                    new List<Vector3> { p1, p2, centroidA },
                                    new List<Vector3> { p4, p2, centroidA },
                                    new List<Vector3> { p1, p4, p2 }
                                };
                                var t1Faces = new List<GeneratedFaceGeometry>();
                                foreach (var fVerts in t1)
                                {
                                    var f = new List<Vector3>(fVerts);
                                    if (OrientOutwardForCentroid(f, (p1 + p4 + centroidA + p2) / 4f))
                                    {
                                        t1Faces.Add(new GeneratedFaceGeometry(faceA, f));
                                    }
                                }
                                generatedSolids.Add(t1Faces);

                                var t2 = new List<List<Vector3>>
                                {
                                    new List<Vector3> { p2, p3, centroidB },
                                    new List<Vector3> { p3, p4, centroidB },
                                    new List<Vector3> { p2, p4, centroidB },
                                    new List<Vector3> { p2, p3, p4 }
                                };
                                var t2Faces = new List<GeneratedFaceGeometry>();
                                foreach (var fVerts in t2)
                                {
                                    var f = new List<Vector3>(fVerts);
                                    if (OrientOutwardForCentroid(f, (p2 + p3 + centroidB + p4) / 4f))
                                    {
                                        t2Faces.Add(new GeneratedFaceGeometry(faceB, f));
                                    }
                                }
                                generatedSolids.Add(t2Faces);

                                var t3 = new List<List<Vector3>>
                                {
                                    new List<Vector3> { p4, p2, centroidA },
                                    new List<Vector3> { p2, p4, centroidB },
                                    new List<Vector3> { p4, centroidA, centroidB },
                                    new List<Vector3> { p2, centroidB, centroidA }
                                };
                                var t3Faces = new List<GeneratedFaceGeometry>();
                                foreach (var fVerts in t3)
                                {
                                    var f = new List<Vector3>(fVerts);
                                    if (OrientOutwardForCentroid(f, (p4 + p2 + centroidA + centroidB) / 4f))
                                    {
                                        t3Faces.Add(new GeneratedFaceGeometry(faceA, f));
                                    }
                                }
                                generatedSolids.Add(t3Faces);
                            }
                            else
                            {
                                var wedgeVerts = new List<List<Vector3>>
                                {
                                    new List<Vector3> { p1, p4, centroidA },
                                    new List<Vector3> { p2, p3, centroidB },
                                    new List<Vector3> { p1, p2, p3, p4 },
                                    new List<Vector3> { p1, centroidA, centroidB, p2 },
                                    new List<Vector3> { p4, p3, centroidB, centroidA }
                                };

                                var wedgeFaces = new List<GeneratedFaceGeometry>();
                                Vector3 wedgeCentroid = (p1 + p2 + p3 + p4 + centroidA + centroidB) / 6f;

                                for (int fIdx = 0; fIdx < wedgeVerts.Count; fIdx++)
                                {
                                    var f = new List<Vector3>(wedgeVerts[fIdx]);
                                    if (OrientOutwardForCentroid(f, wedgeCentroid))
                                    {
                                        var source = fIdx == 0 ? faceA : (fIdx == 1 ? faceB : faceA);
                                        wedgeFaces.Add(new GeneratedFaceGeometry(source, f));
                                    }
                                }
                                generatedSolids.Add(wedgeFaces);
                            }
                        }
                        continue;
                    }

                    var singleSolidFaces = new List<GeneratedFaceGeometry>();
                    bool AddGeneratedFace(Face sourceFace, List<Vector3> verts, out string addReason)
                    {
                        addReason = "";
                        if (!OrientOutward(verts, out var oriented, out var orientReason))
                        {
                            addReason = orientReason;
                            return false;
                        }
                        singleSolidFaces.Add(new GeneratedFaceGeometry(sourceFace, oriented));
                        return true;
                    }

                    if (!AddGeneratedFace(faceA, sliceCapA, out var aReason))
                    {
                        reason = aReason;
                        return false;
                    }
                    if (!AddGeneratedFace(faceB, sliceCapB, out aReason))
                    {
                        reason = aReason;
                        return false;
                    }

                    for (int i = 0; i < n; i++)
                    {
                        var quad = new List<Vector3> { sliceCapA[i], sliceCapB[i], sliceCapB[(i + 1) % n], sliceCapA[(i + 1) % n] };
                        if (!AddGeneratedFace(faceA, quad, out aReason))
                        {
                            reason = aReason;
                            return false;
                        }
                    }

                    generatedSolids.Add(singleSolidFaces);
                    continue;
                }

                var planes = new List<Plane>();
                Plane planeCapA;
                Plane planeCapB;

                if (!TryCreatePlane(sliceCapA[0], sliceCapA[1], sliceCapA[2], out planeCapA))
                {
                    reason = "Face 1 (Blue) generated cap is degenerate.";
                    return false;
                }
                if (!TryCreatePlane(sliceCapB[0], sliceCapB[1], sliceCapB[2], out planeCapB))
                {
                    reason = "Face 2 (Green) generated cap is degenerate.";
                    return false;
                }

                Vector3 connectionDiff = (sliceCapB.Aggregate(Vector3.Zero, (a, b) => a + b) / n) - (sliceCapA.Aggregate(Vector3.Zero, (a, b) => a + b) / n);
                Vector3 connectionDir = connectionDiff.Length() > 1e-4f ? Vector3.Normalize(connectionDiff) : Vector3.UnitZ;

                if (Vector3.Dot(planeCapA.Normal, connectionDir) > 0)
                {
                    planeCapA = new Plane(-planeCapA.Normal, -planeCapA.DistanceFromOrigin);
                }
                if (Vector3.Dot(planeCapB.Normal, connectionDir) < 0)
                {
                    planeCapB = new Plane(-planeCapB.Normal, -planeCapB.DistanceFromOrigin);
                }

                planes.Add(planeCapA);
                planes.Add(planeCapB);

                for (int i = 0; i < n; i++)
                {
                    if (!TryCreatePlane(sliceCapA[i], sliceCapB[i], sliceCapB[(i + 1) % n], out var sidePlane))
                    {
                        reason = $"Side face {i} is degenerate.";
                        return false;
                    }

                    float eval = Vector3.Dot(sidePlane.Normal, solidCentroid) - sidePlane.DistanceFromOrigin;
                    if (eval > 0)
                    {
                        sidePlane = new Plane(-sidePlane.Normal, -sidePlane.DistanceFromOrigin);
                    }
                    planes.Add(sidePlane);
                }

                for (int i = 1; i < otherFaces.Count; i++)
                {
                    var p = otherFaces[i].Plane;
                    var hitPt = (otherHitPoints != null && i - 1 < otherHitPoints.Count)
                        ? otherHitPoints[i - 1]
                        : solidCentroid;
                    float dot = Vector3.Dot(p.Normal, hitPt) - p.DistanceFromOrigin;
                    planes.Add(dot > 0 ? new Plane(-p.Normal, -p.DistanceFromOrigin) : p);
                }

                var filteredPlanes = new List<Plane>();
                foreach (var p in planes)
                {
                    int existingIdx = filteredPlanes.FindIndex(x => x.Normal.EquivalentTo(p.Normal, 0.001f));
                    if (existingIdx >= 0)
                    {
                        if (p.DistanceFromOrigin < filteredPlanes[existingIdx].DistanceFromOrigin)
                        {
                            filteredPlanes[existingIdx] = p;
                        }
                    }
                    else
                    {
                        filteredPlanes.Add(p);
                    }
                }
                planes = filteredPlanes;

                Polyhedron poly;
                try
                {
                    poly = new Polyhedron(planes);
                }
                catch (Exception ex)
                {
                    reason = $"Failed to compute polyhedron intersection: {ex.Message}";
                    return false;
                }

                if (!poly.IsValid() || poly.Polygons.Count < 4)
                {
                    reason = "Clipped brush geometry is invalid or empty. The constraint planes might completely cut off the volume.";
                    return false;
                }

                var polyVerts = poly.Polygons.SelectMany(x => x.Vertices).ToList();
                if (polyVerts.Count > 0)
                {
                    referenceCentroid = polyVerts.Aggregate(Vector3.Zero, (a, b) => a + b) / polyVerts.Count;
                }

                static bool PlaneEquivalentRelaxed(Plane a, Plane b)
                {
                    return a.Normal.EquivalentTo(b.Normal, 0.01f)
                           && Math.Abs(a.DistanceFromOrigin - b.DistanceFromOrigin) < GeometryEpsilon;
                }

                static bool PlaneMatchesEitherDirection(Plane a, Plane b)
                {
                    return PlaneEquivalentRelaxed(a, b)
                           || PlaneEquivalentRelaxed(a, new Plane(-b.Normal, -b.DistanceFromOrigin));
                }

                Face GetMatchingFaceTexture(Plane polyPlane)
                {
                    if (PlaneMatchesEitherDirection(polyPlane, planeCapA)) return faceA;
                    if (PlaneMatchesEitherDirection(polyPlane, planeCapB)) return faceB;

                    for (int i = 1; i < otherFaces.Count; i++)
                    {
                        var constraintFace = otherFaces[i];
                        if (PlaneMatchesEitherDirection(polyPlane, constraintFace.Plane))
                        {
                            return constraintFace;
                        }
                    }
                    return faceA;
                }

                var validPolygons = new List<Polygon>();
                foreach (var polygon in poly.Polygons)
                {
                    bool isPolygonValid = true;
                    foreach (var plane in planes)
                    {
                        foreach (var vert in polygon.Vertices)
                        {
                            float dist = Vector3.Dot(plane.Normal, vert) - plane.DistanceFromOrigin;
                            if (dist > GeometryEpsilon)
                            {
                                isPolygonValid = false;
                                break;
                            }
                        }
                        if (!isPolygonValid) break;
                    }
                    if (isPolygonValid)
                    {
                        validPolygons.Add(polygon);
                    }
                }

                if (validPolygons.Count < 4)
                {
                    reason = "Clipped brush geometry is invalid or empty after filtering out-of-bounds polygons.";
                    return false;
                }

                if (triangulate && !splitMode.StartsWith("One Solid", StringComparison.OrdinalIgnoreCase))
                {
                    Vector3 polyCentroid = validPolygons.SelectMany(x => x.Vertices).Aggregate(Vector3.Zero, (a, b) => a + b) / validPolygons.SelectMany(x => x.Vertices).Count();

                    foreach (var polygon in validPolygons)
                    {
                        var sourceFace = GetMatchingFaceTexture(polygon.Plane);
                        var pyramidFaces = new List<GeneratedFaceGeometry>();

                        var baseVerts = polygon.Vertices.ToList();
                        if (OrientOutwardForCentroid(baseVerts, polyCentroid))
                        {
                            pyramidFaces.Add(new GeneratedFaceGeometry(sourceFace, baseVerts));
                        }

                        int pCount = polygon.Vertices.Count;
                        for (int k = 0; k < pCount; k++)
                        {
                            var sideVerts = new List<Vector3> { polygon.Vertices[k], polygon.Vertices[(k + 1) % pCount], polyCentroid };
                            if (OrientOutwardForCentroid(sideVerts, (polygon.Vertices[k] + polygon.Vertices[(k + 1) % pCount] + polyCentroid) / 3f))
                            {
                                pyramidFaces.Add(new GeneratedFaceGeometry(sourceFace, sideVerts));
                            }
                        }

                        generatedSolids.Add(pyramidFaces);
                    }
                    continue;
                }

                var finalSingleSolidFaces = new List<GeneratedFaceGeometry>();
                foreach (var polygon in validPolygons)
                {
                    var sourceFace = GetMatchingFaceTexture(polygon.Plane);
                    var orientedVerts = polygon.Vertices.ToList();
                    if (!OrientOutwardForCentroid(orientedVerts, referenceCentroid))
                    {
                        reason = "Failed to orient polyhedron face.";
                        return false;
                    }
                    finalSingleSolidFaces.Add(new GeneratedFaceGeometry(sourceFace, orientedVerts));
                }

                generatedSolids.Add(finalSingleSolidFaces);
            }

            return true;
        }

        public static IOperation? Create(
            MapDocument doc,
            Face face1, Solid solid1,
            IReadOnlyList<Face> otherFaces,
            IReadOnlyList<Vector3> otherHitPoints,
            string sizeMode,
            string alignment,
            string depth,
            float offsetA, float offsetB,
            bool usePercentageOffsetA, bool usePercentageOffsetB,
            float thickness, bool usePercentageThick,
            string copySide,
            int validationMode,
            bool triangulate,
            string splitMode,
            int slices,
            int alignmentShiftOffset = 0
        )
        {
            if (otherFaces == null || otherFaces.Count == 0)
            {
                MessageBox.Show("At least 2 faces must be selected (Face 1 and Face 2).", "Brush Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var faceA = face1;
            var faceB = otherFaces[0];
            var solidA = solid1;

            int n = faceA.Vertices.Count;
            int m = faceB.Vertices.Count;

            OnLog?.Invoke($"Initializing brush build. Face 1 (Blue) ID: {faceA.ID} ({n} verts), Face 2 (Green) ID: {faceB.ID} ({m} verts).");
            OnLog?.Invoke($"Parameters: SizeMode={sizeMode}, Alignment={alignment}, Depth={depth}, Thickness={thickness}, Offsets: A={offsetA}, B={offsetB}, Triangulate={triangulate}, SplitMode={splitMode}, Slices={slices}");

            if (n == 0 || m == 0)
            {
                MessageBox.Show("Faces cannot have 0 vertices.", "Brush Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            bool hadFewerThanThreeVertices = (n < 3 || m < 3);
            if (hadFewerThanThreeVertices)
            {
                OnLog?.Invoke($"[WARNING] Face has fewer than 3 vertices (Face 1 (Blue) has {n}, Face 2 (Green) has {m}).");
            }

            if (n != m)
            {
                MessageBox.Show($"Vertex count mismatch! Face 1 (Blue) has {n} vertices, but Face 2 (Green) has {m} vertices.\n\nStrict Mode: Boundary faces must have the same number of vertices.", "Brush Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var caps = GetCaps(
                faceA, faceB,
                sizeMode,
                alignment,
                depth,
                offsetA, offsetB,
                usePercentageOffsetA, usePercentageOffsetB,
                thickness, usePercentageThick,
                copySide,
                alignmentShiftOffset,
                enableLogging: true
            );

            if (caps == null) return null;
            var capA = caps.Value.CapA;
            var capB = caps.Value.CapB;

            var generatedSolids = new List<List<GeneratedFaceGeometry>>();
            if (!TryCreateGeneratedFacesFromCaps(faceA, faceB, otherFaces, otherHitPoints, capA, capB, triangulate, splitMode, slices, generatedSolids, out var geometryReason, enableLogging: true))
            {
                OnLog?.Invoke($"[ERROR] {geometryReason}");
                MessageBox.Show(geometryReason, "Geometry Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var transaction = new Transaction();
            var parentId = solidA?.Hierarchy?.Parent?.ID ?? doc.Map.Root.ID;
            int solidIndex = 0;

            foreach (var solidFaces in generatedSolids)
            {
                var solidId = doc.Map.NumberGenerator.Next("MapObject");
                Solid newSolid = new Solid(solidId);
                newSolid.Data.Add(new ObjectColor(Sledge.Common.Colour.GetRandomBrushColour()));

                foreach (var generatedFace in solidFaces)
                {
                    var newFaceId = doc.Map.NumberGenerator.Next("Face");
                    Face newFace = new Face(newFaceId);
                    newFace.Texture = generatedFace.SourceFace.Texture.Clone();
                    newFace.Vertices.AddRange(generatedFace.Vertices);
                    newFace.Texture.AlignToNormal(newFace.Plane.Normal);
                    newSolid.Data.Add(newFace);
                }

                // Geometry Validation for this solid
                const float MapLimit = 4096f;
                var finalVertices = newSolid.Faces.SelectMany(f => f.Vertices).Distinct().ToList();
                bool exceedsLimit = finalVertices.Any(v => Math.Abs(v.X) > MapLimit || Math.Abs(v.Y) > MapLimit || Math.Abs(v.Z) > MapLimit);
                if (exceedsLimit)
                {
                    string limitMsg = $"Generated brush {solidIndex} exceeds map boundaries (±4096).";
                    OnLog?.Invoke($"[WARNING] {limitMsg}");
                    if (validationMode == 0)
                    {
                        var result = MessageBox.Show(
                            $"{limitMsg} Insert anyway?",
                            "Brush Builder", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (result == DialogResult.No) return null;
                    }
                }

                string invalidReason = "";
                bool isValid = ValidateGeneratedFaces(solidFaces.Select(x => (IReadOnlyList<Vector3>)x.Vertices).ToList(), out invalidReason);

                if (!isValid)
                {
                    OnLog?.Invoke($"[WARNING] Solid {solidIndex} geometry validation failed: {invalidReason}");
                    if (validationMode == 0)
                    {
                        var result = MessageBox.Show(
                            $"Generated brush segment {solidIndex} failed validation checks:\n\n{invalidReason}\n\nInsert invalid brush anyway?",
                            "Brush Builder Validation",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning
                        );
                        if (result == DialogResult.No) return null;
                    }
                }
                else
                {
                    OnLog?.Invoke($"Geometry validation checks for segment {solidIndex} passed successfully.");
                }

                transaction.Add(new Attach(parentId, newSolid));
                solidIndex++;
            }

            return transaction;
        }
    }
}
