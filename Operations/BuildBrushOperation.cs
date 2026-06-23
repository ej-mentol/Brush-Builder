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
    public static partial class BuildBrushOperation
    {
        public static Action<string>? OnLog { get; set; }

        public static IOperation? Create(
            MapDocument doc,
            Face face1, Solid solid1,
            IReadOnlyList<Face> otherFaces,
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
            hadFewerThanThreeVertices = (faceA.Vertices.Count < 3 || faceB.Vertices.Count < 3);

            // Assemble Sledge Solid Primitives
            var solidId = doc.Map.NumberGenerator.Next("MapObject");
            Solid newSolid = new Solid(solidId);
            newSolid.Data.Add(new ObjectColor(Sledge.Common.Colour.GetRandomBrushColour()));

            Vector3 solidCentroid = capA.Concat(capB).Aggregate(Vector3.Zero, (a, b) => a + b) / (capA.Count + capB.Count);
            Vector3 referenceCentroid = solidCentroid;

            // Reverses a face's winding if its computed normal points into the new solid instead of away from it.
            List<Vector3> OrientOutward(List<Vector3> verts)
            {
                var faceCentroid = verts.Aggregate(Vector3.Zero, (a, b) => a + b) / verts.Count;
                var plane = new Plane(verts[0], verts[1], verts[2]);
                if (Vector3.Dot(plane.Normal, faceCentroid - referenceCentroid) < 0)
                {
                    var reversed = new List<Vector3>(verts);
                    reversed.Reverse();
                    return reversed;
                }
                return verts;
            }

            if (otherFaces.Count > 1)
            {
                var planes = new List<Plane>();

                Vector3 centroidCapA = capA.Aggregate(Vector3.Zero, (a, b) => a + b) / capA.Count;
                Vector3 centroidCapB = capB.Aggregate(Vector3.Zero, (a, b) => a + b) / capB.Count;
                Vector3 connectionDelta = centroidCapB - centroidCapA;
                Vector3 connectionDir = connectionDelta.Length() > 1e-3f ? Vector3.Normalize(connectionDelta) : Vector3.UnitZ;

                var planeCapA = new Plane(capA[0], capA[1], capA[2]);
                var planeCapB = new Plane(capB[0], capB[1], capB[2]);

                // Orient cap planes strictly along the connection span direction (outward from solid volume)
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

                // Orient side planes outward from the original solidCentroid
                for (int i = 0; i < n; i++)
                {
                    var sidePlane = new Plane(capA[i], capB[i], capB[(i + 1) % n]);
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
                    planes.Add(new Plane(-p.Normal, -p.DistanceFromOrigin)); // Invert constraint planes because they belong to existing solids
                }

                // Filter out parallel planes pointing in the same direction, keeping the tighter constraint
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

                OnLog?.Invoke($"Constructing Polyhedron from {planes.Count} planes:");
                for (int i = 0; i < planes.Count; i++)
                {
                    OnLog?.Invoke($"  * Plane {i}: Normal={planes[i].Normal:F3}, Dist={planes[i].DistanceFromOrigin:F2}");
                }

                Polyhedron poly;
                try
                {
                    poly = new Polyhedron(planes);
                    OnLog?.Invoke($"Polyhedron constructed successfully. Polygons count: {poly.Polygons.Count}");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[ERROR] Polyhedron construction failed: {ex.Message}");
                    MessageBox.Show($"Failed to compute polyhedron intersection: {ex.Message}", "Geometry Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }

                if (!poly.IsValid() || poly.Polygons.Count < 4)
                {
                    MessageBox.Show("Clipped brush geometry is invalid or empty. The constraint planes might completely cut off the volume.", "Geometry Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }

                var polyVerts = poly.Polygons.SelectMany(x => x.Vertices).ToList();
                if (polyVerts.Count > 0)
                {
                    referenceCentroid = polyVerts.Aggregate(Vector3.Zero, (a, b) => a + b) / polyVerts.Count;
                }

                Face GetMatchingFaceTexture(Plane polyPlane)
                {
                    if (polyPlane.EquivalentTo(planeCapA) || polyPlane.EquivalentTo(new Plane(-planeCapA.Normal, -planeCapA.DistanceFromOrigin)))
                    {
                        return faceA;
                    }
                    if (polyPlane.EquivalentTo(planeCapB) || polyPlane.EquivalentTo(new Plane(-planeCapB.Normal, -planeCapB.DistanceFromOrigin)))
                    {
                        return faceB;
                    }
                    for (int i = 1; i < otherFaces.Count; i++)
                    {
                        var constraintFace = otherFaces[i];
                        if (polyPlane.EquivalentTo(constraintFace.Plane) || polyPlane.EquivalentTo(new Plane(-constraintFace.Plane.Normal, -constraintFace.Plane.DistanceFromOrigin)))
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
                            if (dist > 0.5f)
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
                    MessageBox.Show("Clipped brush geometry is invalid or empty after filtering out-of-bounds polygons.", "Geometry Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }

                foreach (var polygon in validPolygons)
                {
                    var matchingFace = GetMatchingFaceTexture(polygon.Plane);
                    var newFaceId = doc.Map.NumberGenerator.Next("Face");
                    Face newFace = new Face(newFaceId);
                    newFace.Texture = matchingFace.Texture.Clone();
                    newFace.Vertices.AddRange(OrientOutward(polygon.Vertices.ToList()));
                    newFace.Texture.AlignToNormal(newFace.Plane.Normal);
                    newSolid.Data.Add(newFace);
                }
            }
            else
            {
                // Cap A Face
                var faceAId = doc.Map.NumberGenerator.Next("Face");
                Face newFaceA = new Face(faceAId);
                newFaceA.Texture = faceA.Texture.Clone();
                newFaceA.Vertices.AddRange(OrientOutward(capA));
                newFaceA.Texture.AlignToNormal(newFaceA.Plane.Normal);

                // Cap B Face
                var faceBId = doc.Map.NumberGenerator.Next("Face");
                Face newFaceB = new Face(faceBId);
                newFaceB.Texture = faceB.Texture.Clone();
                newFaceB.Vertices.AddRange(OrientOutward(capB));
                newFaceB.Texture.AlignToNormal(newFaceB.Plane.Normal);

                newSolid.Data.Add(newFaceA);
                newSolid.Data.Add(newFaceB);

                // Side Faces (watertight quadrilaterals)
                for (int i = 0; i < n; i++)
                {
                    var sideFaceId = doc.Map.NumberGenerator.Next("Face");
                    Face sideFace = new Face(sideFaceId);
                    sideFace.Texture = faceA.Texture.Clone();

                    var quad = new List<Vector3> { capA[i], capB[i], capB[(i + 1) % n], capA[(i + 1) % n] };
                    sideFace.Vertices.AddRange(OrientOutward(quad));

                    sideFace.Texture.AlignToNormal(sideFace.Plane.Normal);
                    newSolid.Data.Add(sideFace);
                }
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
            bool isValid = ValidateGeometry(capA, capB, out invalidReason);

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
