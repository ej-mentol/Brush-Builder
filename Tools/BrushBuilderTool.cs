using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hotkeys;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Face = Sledge.BspEditor.Primitives.MapObjectData.Face;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace HammerTime.BrushBuilder.Tools
{
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("ZZ")]
    [DefaultHotkey("Shift+B")]
    public class BrushBuilderTool : BaseTool
    {
        public List<(Face Face, Solid Solid, IMapObject Object, Vector3 HitPoint)> SelectedFaces { get; } = new();
        public event Action? SelectionChanged;

        public int AlignmentShiftOffset { get; set; } = 0;

        private string _lastPreviewLogState = "";

        private readonly List<WeakReference<MapViewport>> _viewports = new();

        private void AddViewport(MapViewport vp)
        {
            if (vp == null) return;
            if (!_viewports.Any(w => w.TryGetTarget(out var v) && v == vp))
            {
                _viewports.Add(new WeakReference<MapViewport>(vp));
            }
        }

        public void InvalidateViewports()
        {
            foreach (var wr in _viewports)
            {
                if (wr.TryGetTarget(out var vp) && vp.Control != null && !vp.Control.IsDisposed)
                {
                    vp.Control.Invalidate();
                }
            }
        }

        public Face? HoverFace { get; set; }

        private readonly Lazy<UI.BrushBuilderSettingsContainer> _settings;

        public bool ShowHoverHelper => _settings.Value.ShowHoverPreview;

        private UI.BrushBuilderWindow _window;

        [ImportingConstructor]
        public BrushBuilderTool(
            [Import] Lazy<UI.BrushBuilderSettingsContainer> settings
        )
        {
            _settings = settings;
            Usage = ToolUsage.View3D;
            _window = new UI.BrushBuilderWindow(this, settings);
            Oy.Subscribe<MapViewport>("MapViewport:Created", vp => AddViewport(vp));
        }

        public override async Task ToolSelected()
        {
            var parent = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "Shell") ?? Form.ActiveForm;
            if (parent != null)
            {
                _window.Owner = parent;
            }
            _window.Show();
            await Task.Delay(50);
            await base.ToolSelected();
        }

        public override async Task ToolDeselected()
        {
            _window.Hide();
            ClearSelection();
            await base.ToolDeselected();
        }

        public void ClearSelection()
        {
            SelectedFaces.Clear();
            HoverFace = null;
            _lastPreviewLogState = "";
            LogSelectionState();
            SelectionChanged?.Invoke();
        }

        public override Image GetIcon()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "HammerTime.BrushBuilder.Resources.Tool_BrushBuilder.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var original = Image.FromStream(stream))
                        {
                            var resized = new Bitmap(32, 32);
                            using (var g = Graphics.FromImage(resized))
                            {
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(original, 0, 0, 32, 32);
                            }
                            return resized;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to default system application icon
            }
            return SystemIcons.Application.ToBitmap();
        }

        public override string GetName()
        {
            return "Brush Builder";
        }

        private static (Face Face, Solid Solid, Vector3 HitPoint)? RaycastFace(MapDocument document, PerspectiveCamera camera, int x, int y)
        {
            var (start, end) = camera.CastRayFromScreen(new Vector3(x, y, 0));
            var ray = new Line(start, end);

            var hit = document.Map.Root.GetBoudingBoxIntersectionsForVisibleObjects(ray)
                .OfType<Solid>()
                .Where(s => !s.IsHidden())
                .SelectMany(a => a.Faces.Select(f => new { Face = f, Solid = a }))
                .Select(x => new { x.Face, x.Solid, Intersection = new Polygon(x.Face.Vertices).GetIntersectionPoint(ray) })
                .Where(x => x.Intersection != null)
                .OrderBy(x => (x.Intersection.GetValueOrDefault() - ray.Start).Length())
                .FirstOrDefault();

            return hit == null ? null : (hit.Face, hit.Solid, hit.Intersection.GetValueOrDefault());
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (viewport == null) return;
            AddViewport(viewport);
            if (e.Dragging) return;

            if (!ShowHoverHelper)
            {
                if (HoverFace != null)
                {
                    HoverFace = null;
                    viewport.Control.Invalidate();
                }
                return;
            }

            var hit = RaycastFace(document, camera, e.X, e.Y);
            var newHover = hit?.Face;
            if (newHover != HoverFace)
            {
                HoverFace = newHover;
                viewport.Control.Invalidate();
            }
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (viewport == null) return;
            AddViewport(viewport);
            if (HoverFace != null)
            {
                HoverFace = null;
                viewport.Control.Invalidate();
            }
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (viewport == null || e.Button != MouseButtons.Left) return;
            AddViewport(viewport);

            var hit = RaycastFace(document, camera, e.X, e.Y);
            bool isCtrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool isShift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            bool isMultiSelect = isCtrl || isShift;

            if (hit == null)
            {
                SelectedFaces.Clear();
                viewport.Control.Invalidate();
                SelectionChanged?.Invoke();
                return;
            }

            IMapObject resolvedObject = hit.Value.Solid;
            while (resolvedObject.Hierarchy.Parent != null && !(resolvedObject.Hierarchy.Parent is Root))
            {
                resolvedObject = resolvedObject.Hierarchy.Parent;
            }

            var newFace = hit.Value.Face;
            var hitPoint = hit.Value.HitPoint;

            if (isMultiSelect)
            {
                int index = SelectedFaces.FindIndex(x => x.Face == newFace);
                if (index >= 0)
                {
                    SelectedFaces.RemoveAt(index);
                }
                else
                {
                    SelectedFaces.Add((newFace, hit.Value.Solid, resolvedObject, hitPoint));
                }
            }
            else
            {
                if (SelectedFaces.Count == 0)
                {
                    SelectedFaces.Add((newFace, hit.Value.Solid, resolvedObject, hitPoint));
                }
                else if (SelectedFaces[0].Face == newFace)
                {
                    SelectedFaces.RemoveAt(0);
                }
                else
                {
                    SelectedFaces.Clear();
                    SelectedFaces.Add((newFace, hit.Value.Solid, resolvedObject, hitPoint));
                }
            }
            LogSelectionState();
            viewport.Control.Invalidate();
            SelectionChanged?.Invoke();
        }

        private void LogSelectionState()
        {
            var logAction = Operations.BuildBrushOperation.OnLog;
            if (logAction == null) return;

            logAction($"[Viewport Click] Selected faces count: {SelectedFaces.Count}");
            for (int i = 0; i < SelectedFaces.Count; i++)
            {
                var entry = SelectedFaces[i];
                logAction($"  * Face {i + 1}: ID={entry.Face.ID}, Normal={entry.Face.Plane.Normal:F3}, HitPoint={entry.HitPoint:F1}");
            }

            if (SelectedFaces.Count > 0)
            {
                var min = SelectedFaces[0].HitPoint;
                var max = SelectedFaces[0].HitPoint;
                for (int i = 1; i < SelectedFaces.Count; i++)
                {
                    var pt = SelectedFaces[i].HitPoint;
                    min = Vector3.Min(min, pt);
                    max = Vector3.Max(max, pt);
                }
                var size = max - min;
                float volume = Math.Abs(size.X * size.Y * size.Z);
                logAction($"  * Gizmo (HitPoints) Bounds Size: X={size.X:F1}, Y={size.Y:F1}, Z={size.Z:F1} (Volume: {volume:F1})");
            }
        }

        private void RefreshReferences(MapDocument document)
        {
            int oldCount = SelectedFaces.Count;
            for (int i = SelectedFaces.Count - 1; i >= 0; i--)
            {
                var entry = SelectedFaces[i];
                var currentObj = document.Map.Root.FindByID(entry.Object.ID);
                if (currentObj == null)
                {
                    SelectedFaces.RemoveAt(i);
                    continue;
                }
                var currentSolid = document.Map.Root.FindByID(entry.Solid.ID) as Solid;
                if (currentSolid == null)
                {
                    SelectedFaces.RemoveAt(i);
                    continue;
                }
                var currentFace = currentSolid.Faces.FirstOrDefault(f => f.ID == entry.Face.ID);
                if (currentFace == null)
                {
                    SelectedFaces.RemoveAt(i);
                    continue;
                }
                SelectedFaces[i] = (currentFace, currentSolid, currentObj, entry.HitPoint);
            }
            if (SelectedFaces.Count != oldCount)
            {
                SelectionChanged?.Invoke();
            }
        }

        protected override void Render(MapDocument document, BufferBuilder builder, Sledge.BspEditor.Rendering.Resources.ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);
            RefreshReferences(document);

            var verts = new List<VertexStandard>();
            var indices = new List<uint>();
            var groups = new List<BufferGroup>();

            // Hover Face (Yellow)
            if (ShowHoverHelper && HoverFace != null && !SelectedFaces.Any(x => x.Face == HoverFace))
            {
                var colour = Color.FromArgb(40, Color.Gold).ToVector4();
                RenderFace(HoverFace, colour, verts, indices, groups);
            }

            // Face 3+ (Coral/Orange clip constraints)
            for (int i = 2; i < SelectedFaces.Count; i++)
            {
                var colour = Color.FromArgb(64, Color.Coral).ToVector4();
                RenderFace(SelectedFaces[i].Face, colour, verts, indices, groups);
            }

            if (SelectedFaces.Count >= 2)
            {
                var faceA = SelectedFaces[0].Face;
                var faceB = SelectedFaces[1].Face;

                string sizeMode = _window.SelectedSizeMode;
                string alignment = _window.SelectedAlignment;
                string depth = _window.SelectedDepth;
                float offsetA = _window.SelectedOffsetA;
                bool usePercentageOffsetA = _window.SelectedUsePercentageOffsetA;
                float offsetB = _window.SelectedOffsetB;
                bool usePercentageOffsetB = _window.SelectedUsePercentageOffsetB;
                float thickness = _window.SelectedThickness;
                bool usePercentageThick = _window.SelectedUsePercentageThick;
                int alignmentShiftOffset = AlignmentShiftOffset;

                var otherFaces = SelectedFaces.Skip(1).Select(x => x.Face).ToList();
                // Передаем список точек клика со Skip(1) для точной синхронизации с Create().
                var otherHitPoints = SelectedFaces.Skip(1).Select(x => x.HitPoint).ToList();
                bool hasGeneratedGeometry = Operations.BuildBrushOperation.TryCreateGeneratedFaces(
                    faceA,
                    otherFaces,
                    sizeMode,
                    alignment,
                    depth,
                    offsetA, offsetB,
                    usePercentageOffsetA, usePercentageOffsetB,
                    thickness, usePercentageThick,
                    _window.SelectedCopySide,
                    alignmentShiftOffset,
                    _window.Triangulate,
                    _window.SplitMode,
                    _window.SelectedSlices,
                    out var generatedSolids,
                    out var previewReason,
                    enableLogging: false,
                    otherHitPoints: otherHitPoints
                );

                if (hasGeneratedGeometry)
                {
                    bool allSolidsValid = true;
                    var validationReasons = new List<string>();
                    foreach (var solidFaces in generatedSolids)
                    {
                        if (!Operations.BuildBrushOperation.ValidateGeneratedFaces(
                            solidFaces.Select(x => (IReadOnlyList<Vector3>)x.Vertices).ToList(),
                            out var validationReason))
                        {
                            allSolidsValid = false;
                            if (!string.IsNullOrEmpty(validationReason))
                            {
                                validationReasons.Add(validationReason);
                            }
                        }
                    }

                    var allVerts = generatedSolids.SelectMany(s => s.SelectMany(f => f.Vertices)).ToList();
                    string logState = "";
                    if (allVerts.Count > 0)
                    {
                        var min = allVerts[0];
                        var max = allVerts[0];
                        foreach (var v in allVerts)
                        {
                            min = Vector3.Min(min, v);
                            max = Vector3.Max(max, v);
                        }
                        var size = max - min;
                        float volume = Math.Abs(size.X * size.Y * size.Z);
                        logState = $"Bounds Size: X={size.X:F1}, Y={size.Y:F1}, Z={size.Z:F1} (Volume: {volume:F1}), Solids: {generatedSolids.Count}, Valid: {allSolidsValid}";
                    }
                    else
                    {
                        logState = "No vertices generated";
                    }

                    if (logState != _lastPreviewLogState)
                    {
                        _lastPreviewLogState = logState;
                        Operations.BuildBrushOperation.OnLog?.Invoke($"[Preview Render] {logState}");
                        if (!allSolidsValid && validationReasons.Count > 0)
                        {
                            Operations.BuildBrushOperation.OnLog?.Invoke($"  * Preview Validation warning: {string.Join(" | ", validationReasons)}");
                        }
                    }

                    foreach (var solidFaces in generatedSolids)
                    {
                        bool isSegmentValid = Operations.BuildBrushOperation.ValidateGeneratedFaces(
                            solidFaces.Select(x => (IReadOnlyList<Vector3>)x.Vertices).ToList(),
                            out _);

                        foreach (var generatedFace in solidFaces)
                        {
                            var colour = GetGeneratedFacePreviewColor(generatedFace.SourceFace, faceA, faceB, isSegmentValid);
                            var normal = new Plane(generatedFace.Vertices[0], generatedFace.Vertices[1], generatedFace.Vertices[2]).Normal;
                            RenderCap(generatedFace.Vertices, normal, colour, verts, indices, groups);
                        }
                    }
                }
                else
                {
                    string logState = $"Preview Geometry calculation failed: {previewReason}";
                    if (logState != _lastPreviewLogState)
                    {
                        _lastPreviewLogState = logState;
                        Operations.BuildBrushOperation.OnLog?.Invoke($"[Preview Render] {logState}");
                    }

                    var colourA = Color.FromArgb(128, Color.Red).ToVector4();
                    RenderFace(faceA, colourA, verts, indices, groups);

                    var colourB = Color.FromArgb(128, Color.Red).ToVector4();
                    RenderFace(faceB, colourB, verts, indices, groups);
                }
            }
            else if (SelectedFaces.Count == 1)
            {
                var colour = Color.FromArgb(Operations.BrushBuilderColors.PreviewColor.A, Operations.BrushBuilderColors.Face1).ToVector4();
                RenderFace(SelectedFaces[0].Face, colour, verts, indices, groups);
            }

            // Render gizmos for hit points of selected faces
            for (int i = 0; i < SelectedFaces.Count; i++)
            {
                var entry = SelectedFaces[i];
                var color = i == 0 ? Operations.BrushBuilderColors.Face1 : (i == 1 ? Operations.BrushBuilderColors.Face2 : Operations.BrushBuilderColors.FaceClip);
                RenderHitPointGizmo(entry.HitPoint, entry.Face.Plane.Normal, color.ToVector4(), verts, indices, groups);
            }

            if (verts.Count > 0)
            {
                builder.Append(verts, indices, groups);
            }
        }

        private static Vector4 GetGeneratedFacePreviewColor(Face sourceFace, Face faceA, Face faceB, bool isValid)
        {
            if (!isValid)
            {
                return Color.FromArgb(128, Color.Red).ToVector4();
            }
            if (sourceFace == faceA)
            {
                return Color.FromArgb(Operations.BrushBuilderColors.PreviewColor.A, Operations.BrushBuilderColors.Face1).ToVector4();
            }
            if (sourceFace == faceB)
            {
                return Color.FromArgb(Operations.BrushBuilderColors.PreviewColor.A, Operations.BrushBuilderColors.Face2).ToVector4();
            }
            return Color.FromArgb(Operations.BrushBuilderColors.PreviewColor.A, Operations.BrushBuilderColors.FaceClip).ToVector4();
        }

        private void RenderFace(Face face, Vector4 color, List<VertexStandard> verts, List<uint> indices, List<BufferGroup> groups)
        {
            if (face.Vertices.Count < 3) return;

            var normalOffset = face.Plane.Normal * 0.2f;
            var indOffs = (uint)indices.Count;
            var offs = (uint)verts.Count;
            var centroid = face.Origin + normalOffset;

            verts.Add(new VertexStandard
            {
                Position = centroid,
                Colour = Vector4.One,
                Tint = color,
                Flags = VertexFlags.FlatColour
            });

            verts.AddRange(face.Vertices.Select(x => new VertexStandard
            {
                Position = x + normalOffset,
                Colour = Vector4.One,
                Tint = color,
                Flags = VertexFlags.FlatColour
            }));

            var vertCount = face.Vertices.Count;
            for (uint i = 0; i < vertCount; i++)
            {
                indices.Add(offs);
                indices.Add(offs + 1 + i);
                indices.Add(offs + 1 + (i + 1) % (uint)vertCount);
            }

            groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, face.Origin, indOffs, (uint)(indices.Count - indOffs)));

            // Wireframe Border
            var wfIndOffs = (uint)indices.Count;
            var wfOffs = (uint)verts.Count;
            var outlineColour = new Vector4(color.X, color.Y, color.Z, 1f);

            verts.AddRange(face.Vertices.Select(x => new VertexStandard
            {
                Position = x + normalOffset,
                Colour = outlineColour,
                Tint = Vector4.One
            }));

            for (var i = 0; i < vertCount; i++)
            {
                indices.Add(wfOffs + (uint)i);
                indices.Add(wfOffs + (uint)((i + 1) % vertCount));
            }

            groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, face.Origin, wfIndOffs, (uint)(indices.Count - wfIndOffs)));
        }

        private void RenderCap(List<Vector3> vertices, Vector3 normal, Vector4 color, List<VertexStandard> verts, List<uint> indices, List<BufferGroup> groups)
        {
            if (vertices.Count < 3) return;

            var normalOffset = normal * 0.2f;
            var indOffs = (uint)indices.Count;
            var offs = (uint)verts.Count;
            var centroid = (vertices.Aggregate(Vector3.Zero, (a, b) => a + b) / vertices.Count) + normalOffset;

            verts.Add(new VertexStandard
            {
                Position = centroid,
                Colour = Vector4.One,
                Tint = color,
                Flags = VertexFlags.FlatColour
            });

            verts.AddRange(vertices.Select(x => new VertexStandard
            {
                Position = x + normalOffset,
                Colour = Vector4.One,
                Tint = color,
                Flags = VertexFlags.FlatColour
            }));

            var vertCount = vertices.Count;
            for (uint i = 0; i < vertCount; i++)
            {
                indices.Add(offs);
                indices.Add(offs + 1 + i);
                indices.Add(offs + 1 + (i + 1) % (uint)vertCount);
            }

            groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, centroid, indOffs, (uint)(indices.Count - indOffs)));

            // Wireframe Border
            var wfIndOffs = (uint)indices.Count;
            var wfOffs = (uint)verts.Count;
            var outlineColour = new Vector4(color.X, color.Y, color.Z, 1f);

            verts.AddRange(vertices.Select(x => new VertexStandard
            {
                Position = x + normalOffset,
                Colour = outlineColour,
                Tint = Vector4.One
            }));

            for (var i = 0; i < vertCount; i++)
            {
                indices.Add(wfOffs + (uint)i);
                indices.Add(wfOffs + (uint)((i + 1) % vertCount));
            }

            groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, centroid, wfIndOffs, (uint)(indices.Count - wfIndOffs)));
        }

        private void RenderHitPointGizmo(Vector3 position, Vector3 normal, Vector4 color, List<VertexStandard> verts, List<uint> indices, List<BufferGroup> groups)
        {
            var wfIndOffs = (uint)indices.Count;
            var wfOffs = (uint)verts.Count;
            var outlineColour = new Vector4(color.X, color.Y, color.Z, 1f);

            // 3D Cross (Gizmo)
            float size = 4f;
            verts.Add(new VertexStandard { Position = position - new Vector3(size, 0, 0), Colour = outlineColour, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = position + new Vector3(size, 0, 0), Colour = outlineColour, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = position - new Vector3(0, size, 0), Colour = outlineColour, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = position + new Vector3(0, size, 0), Colour = outlineColour, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = position - new Vector3(0, 0, size), Colour = outlineColour, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = position + new Vector3(0, 0, size), Colour = outlineColour, Tint = Vector4.One });

            // Normal vector line
            float normalLen = 12f;
            var normalColour = Color.Yellow.ToVector4();
            verts.Add(new VertexStandard { Position = position, Colour = normalColour, Tint = Vector4.One });
            verts.Add(new VertexStandard { Position = position + normal * normalLen, Colour = normalColour, Tint = Vector4.One });

            indices.Add(wfOffs + 0); indices.Add(wfOffs + 1);
            indices.Add(wfOffs + 2); indices.Add(wfOffs + 3);
            indices.Add(wfOffs + 4); indices.Add(wfOffs + 5);
            indices.Add(wfOffs + 6); indices.Add(wfOffs + 7);

            groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, position, wfIndOffs, (uint)(indices.Count - wfIndOffs)));
        }
    }
}
