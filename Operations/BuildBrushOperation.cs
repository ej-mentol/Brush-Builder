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
        public static readonly System.Drawing.Color Face1 = System.Drawing.Color.DeepSkyBlue;
        public static readonly System.Drawing.Color Face2 = System.Drawing.Color.LimeGreen;
        public static readonly System.Drawing.Color FaceClip = System.Drawing.Color.Coral;
        public static readonly System.Drawing.Color FaceHover = System.Drawing.Color.Gold;

        // UI active states
        public static readonly System.Drawing.Color ButtonActive = System.Drawing.Color.DodgerBlue;
        public static readonly System.Drawing.Color ButtonActiveFore = System.Drawing.Color.White;
        public static readonly System.Drawing.Color ButtonSwapActive = System.Drawing.Color.Orange;
        public static readonly System.Drawing.Color ButtonSwapActiveFore = System.Drawing.Color.Black;

        // Validation hints
        public static readonly System.Drawing.Color ValidationWarn = System.Drawing.Color.OrangeRed;

        // Цвет превью браша (прозрачный голубой с альфой 64 для лучшего обзора геометрии)
        public static readonly System.Drawing.Color PreviewColor = System.Drawing.Color.FromArgb(64, System.Drawing.Color.DodgerBlue);
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
            out List<GeneratedFaceGeometry> generatedFaces,
            out string reason,
            bool enableLogging = false,
            IReadOnlyList<Vector3>? otherHitPoints = null
        )
        {
            generatedFaces = new List<GeneratedFaceGeometry>();
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

            return TryCreateGeneratedFacesFromCaps(faceA, faceB, otherFaces, otherHitPoints, caps.Value.CapA, caps.Value.CapB, generatedFaces, out reason, enableLogging);
        }

        private static bool TryCreateGeneratedFacesFromCaps(
            Face faceA,
            Face faceB,
            IReadOnlyList<Face> otherFaces,
            IReadOnlyList<Vector3>? otherHitPoints,
            List<Vector3> capA,
            List<Vector3> capB,
            List<GeneratedFaceGeometry> generatedFaces,
            out string reason,
            bool enableLogging
        )
        {
            reason = "";
            generatedFaces.Clear();

            if (capA.Count < 3 || capB.Count < 3 || capA.Count != capB.Count)
            {
                reason = "Generated caps are incomplete.";
                return false;
            }

            int n = capA.Count;
	    Vector3 solidCentroid = capA.Concat(capB).Aggregate(Vector3.Zero, (a, b) => a + b) / (capA.Count + capB.Count);            
            Vector3 referenceCentroid = solidCentroid;

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

            bool AddGeneratedFace(Face sourceFace, List<Vector3> verts, out string addReason)
            {
                addReason = "";
                if (!OrientOutward(verts, out var oriented, out var orientReason))
                {
                    addReason = orientReason;
                    return false;
                }
                generatedFaces.Add(new GeneratedFaceGeometry(sourceFace, oriented));
                return true;
            }

            if (otherFaces.Count <= 1)
            {
                if (!AddGeneratedFace(faceA, capA, out var addReason))
                {
                    reason = addReason;
                    return false;
                }
                if (!AddGeneratedFace(faceB, capB, out addReason))
                {
                    reason = addReason;
                    return false;
                }

                for (int i = 0; i < n; i++)
                {
                    var quad = new List<Vector3> { capA[i], capB[i], capB[(i + 1) % n], capA[(i + 1) % n] };
                    if (!AddGeneratedFace(faceA, quad, out addReason))
                    {
                        reason = addReason;
                        return false;
                    }
                }

                return true;
            }

            var planes = new List<Plane>();

            Vector3 centroidCapA = capA.Aggregate(Vector3.Zero, (a, b) => a + b) / capA.Count;
            Vector3 centroidCapB = capB.Aggregate(Vector3.Zero, (a, b) => a + b) / capB.Count;
            Vector3 connectionDelta = centroidCapB - centroidCapA;
            Vector3 connectionDir = connectionDelta.Length() > 1e-3f ? Vector3.Normalize(connectionDelta) : Vector3.UnitZ;

            if (!TryCreatePlane(capA[0], capA[1], capA[2], out var planeCapA))
            {
                reason = "Face 1 (Blue) generated cap is degenerate.";
                return false;
            }
            if (!TryCreatePlane(capB[0], capB[1], capB[2], out var planeCapB))
            {
                reason = "Face 2 (Green) generated cap is degenerate.";
                return false;
            }

            // Orient cap planes strictly along the connection span direction (outward from solid volume).
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
                if (!TryCreatePlane(capA[i], capB[i], capB[(i + 1) % n], out var sidePlane))
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

            if (enableLogging)
            {
                if (otherHitPoints != null && otherHitPoints.Count > 0)
                {
                    OnLog?.Invoke($"Using {otherHitPoints.Count} hit points for constraint alignment:");
                    for (int i = 0; i < otherHitPoints.Count; i++)
                    {
                        OnLog?.Invoke($"  * HitPoint {i}: {otherHitPoints[i]:F3}");
                    }
                }
                OnLog?.Invoke($"Constructing Polyhedron from {planes.Count} planes:");
                for (int i = 0; i < planes.Count; i++)
                {
                    OnLog?.Invoke($"  * Plane {i}: Normal={planes[i].Normal:F3}, Dist={planes[i].DistanceFromOrigin:F2}");
                }
            }

            Polyhedron poly;
            try
            {
                poly = new Polyhedron(planes);
                if (enableLogging)
                {
                    OnLog?.Invoke($"Polyhedron constructed successfully. Polygons count: {poly.Polygons.Count}");
                }
            }
            catch (Exception ex)
            {
                reason = $"Failed to compute polyhedron intersection: {ex.Message}";
                if (enableLogging) OnLog?.Invoke($"[ERROR] {reason}");
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

            foreach (var polygon in validPolygons)
            {
                var sourceFace = GetMatchingFaceTexture(polygon.Plane);
                if (!AddGeneratedFace(sourceFace, polygon.Vertices.ToList(), out var addReason))
                {
                    reason = addReason;
                    return false;
                }
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
            OnLog?.Invoke($"Parameters: SizeMode={sizeMode}, Alignment={alignment}, Depth={depth}, Thickness={thickness}, Offsets: A={offsetA}, B={offsetB}");

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
            n = capA.Count;

            // Assemble Sledge Solid Primitives
            var solidId = doc.Map.NumberGenerator.Next("MapObject");
            Solid newSolid = new Solid(solidId);
            newSolid.Data.Add(new ObjectColor(Sledge.Common.Colour.GetRandomBrushColour()));

            var generatedFaces = new List<GeneratedFaceGeometry>();
            if (!TryCreateGeneratedFacesFromCaps(faceA, faceB, otherFaces, otherHitPoints, capA, capB, generatedFaces, out var geometryReason, enableLogging: true))
            {
                OnLog?.Invoke($"[ERROR] {geometryReason}");
                MessageBox.Show(geometryReason, "Geometry Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            foreach (var generatedFace in generatedFaces)
            {
                var newFaceId = doc.Map.NumberGenerator.Next("Face");
                Face newFace = new Face(newFaceId);
                newFace.Texture = generatedFace.SourceFace.Texture.Clone();
                newFace.Vertices.AddRange(generatedFace.Vertices);
                newFace.Texture.AlignToNormal(newFace.Plane.Normal);
                newSolid.Data.Add(newFace);
            }

            // Geometry Validation
            const float MapLimit = 4096f;
            var finalVertices = newSolid.Faces.SelectMany(f => f.Vertices).Distinct().ToList();
            bool exceedsLimit = finalVertices.Any(v => Math.Abs(v.X) > MapLimit || Math.Abs(v.Y) > MapLimit || Math.Abs(v.Z) > MapLimit);
            if (exceedsLimit)
            {
                string limitMsg = "Generated brush exceeds map boundaries (±4096).";
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
            bool isValid = ValidateGeneratedFaces(generatedFaces.Select(x => (IReadOnlyList<Vector3>)x.Vertices).ToList(), out invalidReason);

            if (!isValid)
            {
                OnLog?.Invoke($"[WARNING] Geometry validation failed: {invalidReason}");
                if (validationMode == 0)
                {
                    var result = MessageBox.Show(
                        $"Generated brush failed validation checks:\n\n{invalidReason}\n\nInsert invalid brush anyway?",
                        "Brush Builder Validation",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );
                    if (result == DialogResult.No) return null;
                }
            }
            else
            {
                OnLog?.Invoke("Geometry validation checks passed successfully. Solid is valid.");
            }

            var transaction = new Transaction();
            var parentId = solidA?.Hierarchy?.Parent?.ID ?? doc.Map.Root.ID;
            transaction.Add(new Attach(parentId, newSolid));

            return transaction;
        }
    }
}
