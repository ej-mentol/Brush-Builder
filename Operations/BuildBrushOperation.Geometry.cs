using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.DataStructures.Geometric;
using Face = Sledge.BspEditor.Primitives.MapObjectData.Face;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace HammerTime.BrushBuilder.Operations
{
    public static partial class BuildBrushOperation
    {
        public static (List<Vector3> CapA, List<Vector3> CapB)? GetCaps(
            Face faceA, Face faceB,
            string sizeMode,
            string alignment,
            string depth,
            float offsetA, float offsetB,
            bool usePercentageOffsetA, bool usePercentageOffsetB,
            float thickness, bool usePercentageThick,
            string copySide,
            int alignmentShiftOffset,
            bool enableLogging = false
        )
        {
            int n = faceA.Vertices.Count;
            int m = faceB.Vertices.Count;

            if (n != m)
            {
                if (enableLogging)
                {
                    MessageBox.Show($"Vertex count mismatch! Face 1 (Blue) has {n} vertices, but Face 2 (Green) has {m} vertices.", "Brush Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return null;
            }

            if (n == 0 || m == 0)
            {
                if (enableLogging)
                {
                    MessageBox.Show("Faces cannot have 0 vertices.", "Brush Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return null;
            }

            Action<string> log = msg => { if (enableLogging) OnLog?.Invoke(msg); };

            var vertsA = faceA.Vertices.ToList();
            var vertsB = faceB.Vertices.ToList();

            // Padding if fewer than 3 vertices
            bool hadFewerThanThreeVertices = (n < 3 || m < 3);
            if (hadFewerThanThreeVertices)
            {
                while (vertsA.Count < 3)
                {
                    if (vertsA.Count == 1) vertsA.Add(vertsA[0] + Vector3.UnitX);
                    else if (vertsA.Count == 2)
                    {
                        Vector3 dir = Vector3.Normalize(vertsA[1] - vertsA[0]);
                        Vector3 perp = Vector3.Normalize(Vector3.Cross(dir, Math.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY));
                        vertsA.Add(vertsA[0] + perp * 16f);
                    }
                }
                while (vertsB.Count < 3)
                {
                    if (vertsB.Count == 1) vertsB.Add(vertsB[0] + Vector3.UnitX);
                    else if (vertsB.Count == 2)
                    {
                        Vector3 dir = Vector3.Normalize(vertsB[1] - vertsB[0]);
                        Vector3 perp = Vector3.Normalize(Vector3.Cross(dir, Math.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY));
                        vertsB.Add(vertsB[0] + perp * 16f);
                    }
                }
                n = vertsA.Count;
                m = vertsB.Count;
            }

            Vector3 centroidA = faceA.Origin;
            Vector3 centroidB = faceB.Origin;
            Vector3 connectionDelta = centroidB - centroidA;
            float span = connectionDelta.Length();
            if (span < 1e-3f)
            {
                if (enableLogging)
                {
                    MessageBox.Show("Face 1 (Blue) and Face 2 (Green) have the same origin.", "Brush Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return null;
            }

            Vector3 connectionDir = Vector3.Normalize(connectionDelta);

            // Construct projection basis vectors
            Vector3 projUp = Math.Abs(connectionDir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 projU = Vector3.Normalize(Vector3.Cross(connectionDir, projUp));
            Vector3 projV = Vector3.Normalize(Vector3.Cross(connectionDir, projU));

            double ComputeSignedArea(List<Vector3> polygon)
            {
                double area = 0;
                int count = polygon.Count;
                for (int i = 0; i < count; i++)
                {
                    var p1 = polygon[i];
                    var p2 = polygon[(i + 1) % count];
                    double x1 = Vector3.Dot(p1, projU);
                    double y1 = Vector3.Dot(p1, projV);
                    double x2 = Vector3.Dot(p2, projU);
                    double y2 = Vector3.Dot(p2, projV);
                    area += (x1 * y2 - x2 * y1);
                }
                return area;
            }

            double areaA = ComputeSignedArea(vertsA);
            double areaB = ComputeSignedArea(vertsB);

            if ((areaA > 0 && areaB < 0) || (areaA < 0 && areaB > 0))
            {
                vertsB.Reverse();
            }

            int bestShift = 0;
            float minDistanceSum = float.MaxValue;
            for (int shift = 0; shift < n; shift++)
            {
                float distSum = 0;
                for (int i = 0; i < n; i++)
                {
                    distSum += (vertsA[i] - vertsB[(i + shift) % n]).LengthSquared();
                }
                if (distSum < minDistanceSum)
                {
                    minDistanceSum = distSum;
                    bestShift = shift;
                }
            }

            int effectiveShift = (bestShift + alignmentShiftOffset) % n;
            if (effectiveShift < 0) effectiveShift += n;

            var alignedVertsB = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                alignedVertsB.Add(vertsB[(i + effectiveShift) % n]);
            }

            Plane planeA = faceA.Plane;
            Plane planeB = faceB.Plane;

            List<Vector3> capA = new List<Vector3>(n);
            List<Vector3> capB = new List<Vector3>(n);

            Vector3 ProjectPointToPlane(Vector3 point, Plane plane)
            {
                float dist = Vector3.Dot(plane.Normal, point) - plane.DistanceFromOrigin;
                return point - dist * plane.Normal;
            }

            double absAreaA = Math.Abs(areaA);
            double absAreaB = Math.Abs(areaB);

            // Compute standard size mode caps first (prior to thickness or offsets)
            if (sizeMode.StartsWith("Smaller", StringComparison.OrdinalIgnoreCase))
            {
                if (absAreaA <= absAreaB)
                {
                    capA = vertsA;
                    capB = vertsA.Select(v => ProjectPointToPlane(v, planeB)).ToList();
                }
                else
                {
                    capA = alignedVertsB.Select(v => ProjectPointToPlane(v, planeA)).ToList();
                    capB = alignedVertsB;
                }
            }
            else if (sizeMode.StartsWith("Larger", StringComparison.OrdinalIgnoreCase))
            {
                if (absAreaA >= absAreaB)
                {
                    capA = vertsA;
                    capB = vertsA.Select(v => ProjectPointToPlane(v, planeB)).ToList();
                }
                else
                {
                    capA = alignedVertsB.Select(v => ProjectPointToPlane(v, planeA)).ToList();
                    capB = alignedVertsB;
                }
            }
            else if (sizeMode.StartsWith("Use Face A", StringComparison.OrdinalIgnoreCase) || sizeMode.StartsWith("Use Face 1", StringComparison.OrdinalIgnoreCase))
            {
                capA = vertsA;
                capB = vertsA.Select(v => ProjectPointToPlane(v, planeB)).ToList();
            }
            else if (sizeMode.StartsWith("Use Face B", StringComparison.OrdinalIgnoreCase) || sizeMode.StartsWith("Use Face 2", StringComparison.OrdinalIgnoreCase))
            {
                capA = alignedVertsB.Select(v => ProjectPointToPlane(v, planeA)).ToList();
                capB = alignedVertsB;
            }
            else if (sizeMode.StartsWith("Average", StringComparison.OrdinalIgnoreCase))
            {
                var averageVerts = new List<Vector3>(n);
                for (int i = 0; i < n; i++)
                {
                    averageVerts.Add((vertsA[i] + alignedVertsB[i]) * 0.5f);
                }
                capA = averageVerts.Select(v => ProjectPointToPlane(v, planeA)).ToList();
                capB = averageVerts.Select(v => ProjectPointToPlane(v, planeB)).ToList();
            }
            else // Stretch (Default)
            {
                capA = vertsA;
                capB = alignedVertsB;
            }

            // Define local orthogonal axes relative to the target faces
            Vector3 localUpA = GetLongestEdgeDirection(capA);
            localUpA = OrientDirectionConsistent(localUpA, planeA.Normal);
            Vector3 verticalVecA = Vector3.Normalize(localUpA - Vector3.Dot(connectionDir, localUpA) * connectionDir);
            Vector3 horizontalVecA = Vector3.Normalize(Vector3.Cross(verticalVecA, connectionDir));
            horizontalVecA = OrientHorizontalConsistent(horizontalVecA);

            // Apply Copy Side if specified (modifies initial profile)
            bool hasCopySide = !string.IsNullOrEmpty(copySide) && !copySide.Equals("None", StringComparison.OrdinalIgnoreCase);
            if (hasCopySide)
            {
                ApplyCopySide(capA, planeA, copySide);
                ApplyCopySide(capB, planeB, copySide);
            }

            // Apply Alignment translations if Alignment is not C (Center)
            void AlignCap(List<Vector3> capToAlign, List<Vector3> targetShape, Plane targetPlane)
            {
                Vector3 normal = targetPlane.Normal;
                Vector3 localUp = GetLongestEdgeDirection(capToAlign);
                localUp = OrientDirectionConsistent(localUp, normal);
                Vector3 localRight = Vector3.Normalize(Vector3.Cross(localUp, normal));
                localRight = OrientHorizontalConsistent(localRight);

                Vector3 shift = Vector3.Zero;

                if (alignment.Equals("Top", StringComparison.OrdinalIgnoreCase) || alignment.Equals("U", StringComparison.OrdinalIgnoreCase))
                {
                    float maxTarget = targetShape.Max(v => Vector3.Dot(v, localUp));
                    float maxCap = capToAlign.Max(v => Vector3.Dot(v, localUp));
                    shift = (maxTarget - maxCap) * localUp;
                }
                else if (alignment.Equals("Bottom", StringComparison.OrdinalIgnoreCase) || alignment.Equals("D", StringComparison.OrdinalIgnoreCase))
                {
                    float minTarget = targetShape.Min(v => Vector3.Dot(v, localUp));
                    float minCap = capToAlign.Min(v => Vector3.Dot(v, localUp));
                    shift = (minTarget - minCap) * localUp;
                }
                else if (alignment.Equals("Left", StringComparison.OrdinalIgnoreCase) || alignment.Equals("L", StringComparison.OrdinalIgnoreCase))
                {
                    float minTarget = targetShape.Min(v => Vector3.Dot(v, localRight));
                    float minCap = capToAlign.Min(v => Vector3.Dot(v, localRight));
                    shift = (minTarget - minCap) * localRight;
                }
                else if (alignment.Equals("Right", StringComparison.OrdinalIgnoreCase) || alignment.Equals("R", StringComparison.OrdinalIgnoreCase))
                {
                    float maxTarget = targetShape.Max(v => Vector3.Dot(v, localRight));
                    float maxCap = capToAlign.Max(v => Vector3.Dot(v, localRight));
                    shift = (maxTarget - maxCap) * localRight;
                }

                for (int i = 0; i < capToAlign.Count; i++)
                {
                    capToAlign[i] += shift;
                }
            }

            if (!alignment.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                AlignCap(capA, vertsA, planeA);
                AlignCap(capB, alignedVertsB, planeB);
            }

            // Apply Thickness & Positioning automatically
            if (thickness > 0)
            {
                bool isAlongSpan = alignment.Equals("C", StringComparison.OrdinalIgnoreCase) && !depth.Equals("Mid", StringComparison.OrdinalIgnoreCase);

                if (isAlongSpan)
                {
                    float actualThickness = usePercentageThick ? (thickness / 100.0f) * span : thickness;
                    if (depth.Equals("F1", StringComparison.OrdinalIgnoreCase) || depth.Equals("Face 1", StringComparison.OrdinalIgnoreCase))
                    {
                        capB = capA.Select(v => v + actualThickness * connectionDir).ToList();
                    }
                    else if (depth.Equals("F2", StringComparison.OrdinalIgnoreCase) || depth.Equals("Face 2", StringComparison.OrdinalIgnoreCase))
                    {
                        capA = capB.Select(v => v - actualThickness * connectionDir).ToList();
                    }
                }
                else
                {
                    // Transverse Thickness (Horizontal/Vertical)
                    bool isVertical = alignment.Equals("U", StringComparison.OrdinalIgnoreCase) || alignment.Equals("D", StringComparison.OrdinalIgnoreCase);

                    void ApplyTransverseThickness(List<Vector3> cap, Plane plane)
                    {
                        if (cap.Count == 0) return;

                        Vector3 localUp = GetLongestEdgeDirection(cap);
                        if (localUp.LengthSquared() < 0.001f)
                        {
                            Vector3 projUpLoc = Math.Abs(plane.Normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
                            localUp = Vector3.Normalize(projUpLoc - Vector3.Dot(projUpLoc, plane.Normal) * plane.Normal);
                        }
                        localUp = OrientDirectionConsistent(localUp, plane.Normal);
                        Vector3 localRight = Vector3.Normalize(Vector3.Cross(localUp, plane.Normal));
                        localRight = OrientHorizontalConsistent(localRight);

                        Vector3 thickVec = isVertical ? localUp : localRight;

                        var coords = cap.Select(v => Vector3.Dot(v, thickVec)).ToList();
                        float min = coords.Min();
                        float max = coords.Max();
                        float width = max - min;
                        if (width > 1e-4f)
                        {
                            float actualThickness = usePercentageThick ? (thickness / 100f) * width : thickness;

                            if (cap.Count == 4)
                            {
                                var sorted = cap.Select((v, idx) => new { Index = idx, U = Vector3.Dot(v, localUp), R = Vector3.Dot(v, localRight) })
                                                .OrderBy(x => x.U)
                                                .ToList();

                                if (isVertical)
                                {
                                    int idx_BL, idx_BR, idx_TL, idx_TR;
                                    if (sorted[0].R < sorted[1].R)
                                    {
                                        idx_BL = sorted[0].Index;
                                        idx_BR = sorted[1].Index;
                                    }
                                    else
                                    {
                                        idx_BL = sorted[1].Index;
                                        idx_BR = sorted[0].Index;
                                    }

                                    if (sorted[2].R < sorted[3].R)
                                    {
                                        idx_TL = sorted[2].Index;
                                        idx_TR = sorted[3].Index;
                                    }
                                    else
                                    {
                                        idx_TL = sorted[3].Index;
                                        idx_TR = sorted[2].Index;
                                    }

                                    void AdjustPairVertical(int idxBot, int idxTop)
                                    {
                                        float uBot = Vector3.Dot(cap[idxBot], localUp);
                                        float uTop = Vector3.Dot(cap[idxTop], localUp);
                                        float center = (uBot + uTop) * 0.5f;

                                        float uBotNew = uBot;
                                        float uTopNew = uTop;

                                        if (alignment.Equals("U", StringComparison.OrdinalIgnoreCase))
                                        {
                                            uBotNew = uTop - actualThickness;
                                        }
                                        else if (alignment.Equals("D", StringComparison.OrdinalIgnoreCase))
                                        {
                                            uTopNew = uBot + actualThickness;
                                        }
                                        else // Center ("C")
                                        {
                                            uBotNew = center - actualThickness * 0.5f;
                                            uTopNew = center + actualThickness * 0.5f;
                                        }

                                        cap[idxBot] += (uBotNew - uBot) * localUp;
                                        cap[idxTop] += (uTopNew - uTop) * localUp;
                                    }

                                    AdjustPairVertical(idx_BL, idx_TL);
                                    AdjustPairVertical(idx_BR, idx_TR);
                                }
                                else
                                {
                                    int idx_BL, idx_BR, idx_TL, idx_TR;
                                    if (sorted[0].R < sorted[1].R)
                                    {
                                        idx_BL = sorted[0].Index;
                                        idx_BR = sorted[1].Index;
                                    }
                                    else
                                    {
                                        idx_BL = sorted[1].Index;
                                        idx_BR = sorted[0].Index;
                                    }

                                    if (sorted[2].R < sorted[3].R)
                                    {
                                        idx_TL = sorted[2].Index;
                                        idx_TR = sorted[3].Index;
                                    }
                                    else
                                    {
                                        idx_TL = sorted[3].Index;
                                        idx_TR = sorted[2].Index;
                                    }

                                    void AdjustPairHorizontal(int idxLeft, int idxRight)
                                    {
                                        float rLeft = Vector3.Dot(cap[idxLeft], localRight);
                                        float rRight = Vector3.Dot(cap[idxRight], localRight);
                                        float center = (rLeft + rRight) * 0.5f;

                                        float rLeftNew = rLeft;
                                        float rRightNew = rRight;

                                        if (alignment.Equals("R", StringComparison.OrdinalIgnoreCase))
                                        {
                                            rLeftNew = rRight - actualThickness;
                                        }
                                        else if (alignment.Equals("L", StringComparison.OrdinalIgnoreCase))
                                        {
                                            rRightNew = rLeft + actualThickness;
                                        }
                                        else // Center ("C")
                                        {
                                            rLeftNew = center - actualThickness * 0.5f;
                                            rRightNew = center + actualThickness * 0.5f;
                                        }

                                        cap[idxLeft] += (rLeftNew - rLeft) * localRight;
                                        cap[idxRight] += (rRightNew - rRight) * localRight;
                                    }

                                    AdjustPairHorizontal(idx_BL, idx_BR);
                                    AdjustPairHorizontal(idx_TL, idx_TR);
                                }
                            }
                            else
                            {
                                float start = min;
                                if (alignment.Equals("R", StringComparison.OrdinalIgnoreCase) || alignment.Equals("U", StringComparison.OrdinalIgnoreCase))
                                {
                                    start = max - actualThickness;
                                }
                                else if (alignment.Equals("C", StringComparison.OrdinalIgnoreCase))
                                {
                                    start = (min + max - actualThickness) * 0.5f;
                                }

                                for (int i = 0; i < cap.Count; i++)
                                {
                                    float c = coords[i];
                                    float cNew = start + (c - min) * (actualThickness / width);
                                    cap[i] += (cNew - c) * thickVec;
                                }
                            }
                        }
                    }

                    ApplyTransverseThickness(capA, planeA);
                    ApplyTransverseThickness(capB, planeB);
                }
            }

            // Apply Inset F1 and Inset F2 (axial offsets along normals)
            float actualOffsetA = usePercentageOffsetA ? (offsetA / 100.0f) * span : offsetA;
            float actualOffsetB = usePercentageOffsetB ? (offsetB / 100.0f) * span : offsetB;

            if (actualOffsetA != 0)
            {
                Vector3 shiftA = -actualOffsetA * planeA.Normal;
                for (int i = 0; i < n; i++) capA[i] += shiftA;
            }
            if (actualOffsetB != 0)
            {
                Vector3 shiftB = -actualOffsetB * planeB.Normal;
                for (int i = 0; i < n; i++) capB[i] += shiftB;
            }

            return (capA, capB);
        }

        public static Vector3 GetLongestEdgeDirection(List<Vector3> vertices)
        {
            if (vertices == null || vertices.Count < 2) return Vector3.UnitZ;
            Vector3 longestEdge = Vector3.Zero;
            float maxLenSq = -1f;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 p1 = vertices[i];
                Vector3 p2 = vertices[(i + 1) % vertices.Count];
                Vector3 edge = p2 - p1;
                float lenSq = edge.LengthSquared();
                if (lenSq > maxLenSq)
                {
                    maxLenSq = lenSq;
                    longestEdge = edge;
                }
            }
            return maxLenSq > 1e-5f ? Vector3.Normalize(longestEdge) : Vector3.UnitZ;
        }

        public static Vector3 OrientDirectionConsistent(Vector3 dir, Vector3 normal)
        {
            bool isHorizontalFace = Math.Abs(normal.Z) > 0.707f;
            if (isHorizontalFace)
            {
                if (dir.Y < -1e-4f || (Math.Abs(dir.Y) < 1e-4f && dir.X < -1e-4f))
                {
                    return -dir;
                }
            }
            else
            {
                if (dir.Z < -1e-4f || (Math.Abs(dir.Z) < 1e-4f && dir.Y < -1e-4f))
                {
                    return -dir;
                }
            }
            return dir;
        }

        public static Vector3 OrientHorizontalConsistent(Vector3 dir)
        {
            if (dir.X < -1e-4f || (Math.Abs(dir.X) < 1e-4f && dir.Y < -1e-4f))
            {
                return -dir;
            }
            return dir;
        }

        private static void ApplyCopySide(List<Vector3> cap, Plane plane, string copySide)
        {
            if (cap == null || cap.Count != 4 || string.IsNullOrEmpty(copySide) || copySide.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Vector3 localUp = GetLongestEdgeDirection(cap);
            if (localUp.LengthSquared() < 0.001f)
            {
                Vector3 projUp = Math.Abs(plane.Normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
                localUp = Vector3.Normalize(projUp - Vector3.Dot(projUp, plane.Normal) * plane.Normal);
            }
            localUp = OrientDirectionConsistent(localUp, plane.Normal);
            Vector3 localRight = Vector3.Normalize(Vector3.Cross(localUp, plane.Normal));
            localRight = OrientHorizontalConsistent(localRight);

            var sorted = cap.Select((v, idx) => new { Index = idx, U = Vector3.Dot(v, localUp), R = Vector3.Dot(v, localRight) })
                            .OrderBy(x => x.U)
                            .ToList();

            int idx_BL, idx_BR, idx_TL, idx_TR;

            if (sorted[0].R < sorted[1].R)
            {
                idx_BL = sorted[0].Index;
                idx_BR = sorted[1].Index;
            }
            else
            {
                idx_BL = sorted[1].Index;
                idx_BR = sorted[0].Index;
            }

            if (sorted[2].R < sorted[3].R)
            {
                idx_TL = sorted[2].Index;
                idx_TR = sorted[3].Index;
            }
            else
            {
                idx_TL = sorted[3].Index;
                idx_TR = sorted[2].Index;
            }

            float r_BL = Vector3.Dot(cap[idx_BL], localRight);
            float r_BR = Vector3.Dot(cap[idx_BR], localRight);
            float r_TL = Vector3.Dot(cap[idx_TL], localRight);
            float r_TR = Vector3.Dot(cap[idx_TR], localRight);

            float u_BL = Vector3.Dot(cap[idx_BL], localUp);
            float u_BR = Vector3.Dot(cap[idx_BR], localUp);
            float u_TL = Vector3.Dot(cap[idx_TL], localUp);
            float u_TR = Vector3.Dot(cap[idx_TR], localUp);

            if (copySide.Equals("L", StringComparison.OrdinalIgnoreCase))
            {
                float r_TR_new = r_BR + (r_TL - r_BL);
                cap[idx_TR] += (r_TR_new - r_TR) * localRight;
            }
            else if (copySide.Equals("R", StringComparison.OrdinalIgnoreCase))
            {
                float r_TL_new = r_BL + (r_TR - r_BR);
                cap[idx_TL] += (r_TL_new - r_TL) * localRight;
            }
            else if (copySide.Equals("U", StringComparison.OrdinalIgnoreCase))
            {
                float u_BR_new = u_BL + (u_TR - u_TL);
                cap[idx_BR] += (u_BR_new - u_BR) * localUp;
            }
            else if (copySide.Equals("D", StringComparison.OrdinalIgnoreCase))
            {
                float u_TR_new = u_TL + (u_BR - u_BL);
                cap[idx_TR] += (u_TR_new - u_TR) * localUp;
            }
        }
    }
}
