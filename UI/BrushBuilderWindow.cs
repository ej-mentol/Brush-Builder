using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Shell;

namespace HammerTime.BrushBuilder.UI
{
    public class BrushBuilderWindow : Form
    {
        private readonly Tools.BrushBuilderTool _tool;

        private Button btnBuild = null!;
        private Button btnSwap = null!;
        private ListBox lstFaces = null!;
        private Label lblInstruction = null!;
        private NumericUpDown numShiftOffset = null!;
        private Button btnResetShiftOffset = null!;
        private readonly List<long> _cachedFaceIds = new();
        private readonly List<long> _originalFaceIds = new();

        // Size Mode buttons (Profile)
        private Button btnModeLoft = null!;
        private Button btnModeF1 = null!;
        private Button btnModeF2 = null!;
        private Button btnModeMin = null!;
        private Button btnModeMax = null!;
        private string _selectedSizeMode = "Stretch (Loft)";

        // Copy Profile Side buttons (Rose Grid)
        private Button[] copyRoseButtons = null!;
        private string _selectedCopySide = "None";

        // Side Alignment buttons
        private Button btnAlignU = null!;
        private Button btnAlignD = null!;
        private Button btnAlignL = null!;
        private Button btnAlignR = null!;
        private Button btnAlignC = null!;
        private string _selectedAlignment = "C";

        // Depth Alignment buttons
        private Button btnDepthF1 = null!;
        private Button btnDepthMid = null!;
        private Button btnDepthF2 = null!;
        private string _selectedDepth = "Mid";

        // Numeric fields with resets and toggle buttons
        private NumericUpDown numThickness = null!;
        private Button btnResetThickness = null!;
        private Button btnUsePercentageThick = null!;
        private bool _usePercentageThick = false;
        private Label lblThicknessPreview = null!;

        private NumericUpDown numOffsetA = null!;
        private Button btnResetOffsetA = null!;
        private Button btnUsePercentageOffsetA = null!;
        private bool _usePercentageOffsetA = false;
        private Label lblOffsetAPreview = null!;

        private NumericUpDown numOffsetB = null!;
        private Button btnResetOffsetB = null!;
        private Button btnUsePercentageOffsetB = null!;
        private bool _usePercentageOffsetB = false;
        private Label lblOffsetBPreview = null!;

        private CheckBox chkTriangulate = null!;
        private ComboBox cmbSplitMode = null!;
        private NumericUpDown numSlices = null!;
        private TextBox txtLog = null!;
        private Label lblProfileShapeTip = null!;
        private ComboBox cmbProfiles = null!;
        private readonly List<ToolProfile> _profiles = new();

        private bool _isBuilding = false;
        private readonly Lazy<BrushBuilderSettingsContainer> _settings;

        public BrushBuilderWindow(Tools.BrushBuilderTool tool, Lazy<BrushBuilderSettingsContainer> settings)
        {
            _tool = tool;
            _settings = settings;
            _cachedFaceIds.AddRange(_tool.SelectedFaces.Select(x => (long)x.Face.ID));
            _originalFaceIds.AddRange(_cachedFaceIds);
            InitializeComponent();
            Operations.BuildBrushOperation.OnLog = AppendLog;
            LoadProfilesList();

            Oy.Subscribe<bool>("Theme:Changed", dark => {
                this.InvokeLater(() => {
                    Sledge.Shell.Registers.DialogRegister.ColorControlsRecursively(this, dark);
                    UpdateAlignmentButtons();
                    UpdateDepthButtons();
                    UpdateSizeModeButtons();
                    UpdateCopySideButtons();
                    UpdateUnitToggleButtonsColors();
                });
            });

            Oy.Subscribe<object>("SettingsChanged", _ => {
                this.InvokeLater(() => {
                    _tool.InvalidateViewports();
                });
            });

            _tool.SelectionChanged += OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            this.InvokeLater(() => UpdateSelectionStatus());
        }

        private void InitializeComponent()
        {
            this.Text = "Brush Builder";
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.Size = new Size(720, 620);
            this.MinimumSize = new Size(680, 580);

            // Left Column layout (Actions, Profile, Triangulation, Thickness & Position)
            var pnlLeft = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(4)
            };
            pnlLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 65f));   // Build & Swap Actions
            pnlLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 162f));  // Profile modes & Copy Side
            pnlLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));   // Triangulation Group
            pnlLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Thickness & Position

            // 1. Actions Panel (Top-Left)
            var grpActions = new GroupBox { Text = "Actions", Dock = DockStyle.Fill, Margin = new Padding(2) };
            var gridActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(2) };
            gridActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            gridActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            btnBuild = new Button { Text = "Build Brush", Font = new Font(this.Font, FontStyle.Bold), Dock = DockStyle.Fill, Height = 28, Margin = new Padding(2) };
            var btnResetAll = new Button { Text = "Reset All", Dock = DockStyle.Fill, Height = 28, Margin = new Padding(2) };
            btnResetAll.Click += (s, e) => ResetAllSettings();
            gridActions.Controls.Add(btnBuild, 0, 0);
            gridActions.Controls.Add(btnResetAll, 1, 0);
            grpActions.Controls.Add(gridActions);
            pnlLeft.Controls.Add(grpActions, 0, 0);

            // 2. Profile GroupBox
            var grpProfile = new GroupBox { Text = "Profile", Dock = DockStyle.Fill, Margin = new Padding(2) };
            var pnlProfileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = new Padding(0) };
            pnlProfileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f)); // Left: Rose (84px width + gap)
            pnlProfileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // Right: Settings
            pnlProfileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));        // Rose & Size Mode height
            pnlProfileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));        // Rot Offset height
            pnlProfileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));        // Tip label height

            // Left Side: Copy Side Rose Grid (3x3)
            copyRoseButtons = CreateCopyRoseGrid();
            var copyRoseContainer = WrapRoseGrid(copyRoseButtons);
            pnlProfileLayout.Controls.Add(copyRoseContainer, 0, 0);
            pnlProfileLayout.SetRowSpan(copyRoseContainer, 2); // Spans over rows 0 and 1

            // Right Side: Size Mode Buttons arranged as 2 columns, 3 rows
            var gridSizeMode = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = new Padding(0) };
            gridSizeMode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            gridSizeMode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            gridSizeMode.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            gridSizeMode.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            gridSizeMode.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

            btnModeLoft = new Button { Text = "Loft", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnModeF1 = new Button { Text = "F1", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnModeF2 = new Button { Text = "F2", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnModeMin = new Button { Text = "Min", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnModeMax = new Button { Text = "Max", Dock = DockStyle.Fill, Margin = new Padding(1) };

            btnModeLoft.Click += (s, e) => SetSizeMode("Stretch (Loft)");
            btnModeF1.Click += (s, e) => SetSizeMode("Use Face 1 (Blue)");
            btnModeF2.Click += (s, e) => SetSizeMode("Use Face 2 (Green)");
            btnModeMin.Click += (s, e) => SetSizeMode("Smaller Face");
            btnModeMax.Click += (s, e) => SetSizeMode("Larger Face");

            gridSizeMode.Controls.Add(btnModeLoft, 0, 0);
            gridSizeMode.SetColumnSpan(btnModeLoft, 2);
            gridSizeMode.Controls.Add(btnModeF1, 0, 1);
            gridSizeMode.Controls.Add(btnModeF2, 1, 1);
            gridSizeMode.Controls.Add(btnModeMin, 0, 2);
            gridSizeMode.Controls.Add(btnModeMax, 1, 2);

            pnlProfileLayout.Controls.Add(gridSizeMode, 1, 0);

            lblProfileShapeTip = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Font = new Font(this.Font.FontFamily, 7.5f), Margin = new Padding(8, 2, 8, 0) };

            var flowRotOffset = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
            var lblRotOffset = new Label { Text = "Rot Offset:", AutoSize = true, Margin = new Padding(0, 5, 4, 0) };
            numShiftOffset = new NumericUpDown { Minimum = 0, Maximum = 0, Value = 0, Width = 60, Enabled = false, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 2, 4, 0) };
            int inputHeight = numShiftOffset.PreferredHeight;
            btnResetShiftOffset = new Button { Text = "↺", Width = 22, Height = inputHeight, Margin = new Padding(0, 2, 0, 0), FlatStyle = FlatStyle.System };
            
            btnResetShiftOffset.Click += (s, e) => {
                numShiftOffset.Value = 0m;
                _tool.AlignmentShiftOffset = 0;
                UpdateSettingsCache();
                _tool.InvalidateViewports();
            };
            
            flowRotOffset.Controls.Add(lblRotOffset);
            flowRotOffset.Controls.Add(numShiftOffset);
            flowRotOffset.Controls.Add(btnResetShiftOffset);

            pnlProfileLayout.Controls.Add(flowRotOffset, 1, 1); // Placed next to the Rose, under gridSizeMode
            pnlProfileLayout.Controls.Add(lblProfileShapeTip, 0, 2);
            pnlProfileLayout.SetColumnSpan(lblProfileShapeTip, 2);
            grpProfile.Controls.Add(pnlProfileLayout);
            pnlLeft.Controls.Add(grpProfile, 0, 1);

            // 3. Thickness & Positioning GroupBox
            var grpThickness = new GroupBox { Text = "Thickness & Positioning", Dock = DockStyle.Fill, Margin = new Padding(2) };
            
            var gridPositioning = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 10, RowCount = 4, Padding = new Padding(2) };
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30f)); // 0: Side compass Left (L)
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30f)); // 1: Side compass Middle (U, C, D)
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30f)); // 2: Side compass Right (R)
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10f)); // 3: Vertical Separator
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f)); // 4: Label
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f)); // 5: Numeric Box
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26f)); // 6: Reset ↺
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46f)); // 7: Units/% toggle button
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // 8: Preview label
            gridPositioning.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22f)); // 9: Color block

            gridPositioning.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            gridPositioning.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            gridPositioning.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            gridPositioning.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            btnAlignU = new Button { Text = "↑", Tag = "U", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnAlignL = new Button { Text = "←", Tag = "L", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnAlignC = new Button { Text = "N", Tag = "C", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnAlignR = new Button { Text = "→", Tag = "R", Dock = DockStyle.Fill, Margin = new Padding(1) };
            btnAlignD = new Button { Text = "↓", Tag = "D", Dock = DockStyle.Fill, Margin = new Padding(1) };

            btnAlignU.Click += (s, e) => SetAlignment("U");
            btnAlignL.Click += (s, e) => SetAlignment("L");
            btnAlignC.Click += (s, e) => SetAlignment("C");
            btnAlignR.Click += (s, e) => SetAlignment("R");
            btnAlignD.Click += (s, e) => SetAlignment("D");

            // Row 0: Thickness inputs
            gridPositioning.Controls.Add(btnAlignU, 1, 0);

            var lblThick = new Label { Text = "Thickness:", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(0) };
            numThickness = new NumericUpDown { Minimum = 0m, Maximum = 1000m, Value = 0m, Width = 64, Anchor = AnchorStyles.None, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0) };
            btnResetThickness = new Button { Text = "↺", Width = 22, Height = inputHeight, Margin = new Padding(0), FlatStyle = FlatStyle.System, Anchor = AnchorStyles.None };
            btnUsePercentageThick = new Button { Text = "units", Width = 42, Height = inputHeight, Margin = new Padding(0), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.None };
            lblThicknessPreview = new Label { Text = "", ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(2, 0, 0, 0) };

            btnResetThickness.Click += (s, e) => { numThickness.Value = 0m; UpdateThicknessPreview(); _tool.InvalidateViewports(); };
            btnUsePercentageThick.Click += (s, e) => {
                _usePercentageThick = !_usePercentageThick;
                OnUnitToggleChanged(btnUsePercentageThick, _usePercentageThick, numThickness, 0m, 100m, 0m, 1000m);
                UpdateThicknessPreview();
            };
            numThickness.ValueChanged += (s, e) => { UpdateThicknessPreview(); UpdateSettingsCache(); _tool.InvalidateViewports(); };

            // Add vertical separator line between compass and values
            var sepPositioning = new Panel { BackColor = SystemColors.ControlDark, Width = 1, Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2) };
            gridPositioning.Controls.Add(sepPositioning, 3, 0);
            gridPositioning.SetRowSpan(sepPositioning, 3);

            gridPositioning.Controls.Add(lblThick, 4, 0);
            gridPositioning.Controls.Add(numThickness, 5, 0);
            gridPositioning.Controls.Add(btnResetThickness, 6, 0);
            gridPositioning.Controls.Add(btnUsePercentageThick, 7, 0);
            gridPositioning.Controls.Add(lblThicknessPreview, 8, 0);

            // Row 1: Inset F1 inputs
            gridPositioning.Controls.Add(btnAlignL, 0, 1);
            gridPositioning.Controls.Add(btnAlignC, 1, 1);
            gridPositioning.Controls.Add(btnAlignR, 2, 1);

            var lblInsetF1 = new Label { Text = "Inset F1:", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(0) };
            numOffsetA = new NumericUpDown { Minimum = -1000m, Maximum = 1000m, Value = 0m, Width = 64, Anchor = AnchorStyles.None, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0) };
            btnResetOffsetA = new Button { Text = "↺", Width = 22, Height = inputHeight, Margin = new Padding(0), FlatStyle = FlatStyle.System, Anchor = AnchorStyles.None };
            btnUsePercentageOffsetA = new Button { Text = "units", Width = 42, Height = inputHeight, Margin = new Padding(0), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.None };
            lblOffsetAPreview = new Label { Text = "", ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(2, 0, 0, 0) };
            var pnlColorA = new Panel { BackColor = Operations.BrushBuilderColors.Face1, Width = 12, Height = 12, Anchor = AnchorStyles.None, Margin = new Padding(0) };

            btnResetOffsetA.Click += (s, e) => { numOffsetA.Value = 0m; UpdateOffsetAPreview(); _tool.InvalidateViewports(); };
            btnUsePercentageOffsetA.Click += (s, e) => {
                _usePercentageOffsetA = !_usePercentageOffsetA;
                OnUnitToggleChanged(btnUsePercentageOffsetA, _usePercentageOffsetA, numOffsetA, -100m, 100m, -1000m, 1000m);
                UpdateOffsetAPreview();
            };
            numOffsetA.ValueChanged += (s, e) => { UpdateOffsetAPreview(); UpdateSettingsCache(); _tool.InvalidateViewports(); };

            gridPositioning.Controls.Add(lblInsetF1, 4, 1);
            gridPositioning.Controls.Add(numOffsetA, 5, 1);
            gridPositioning.Controls.Add(btnResetOffsetA, 6, 1);
            gridPositioning.Controls.Add(btnUsePercentageOffsetA, 7, 1);
            gridPositioning.Controls.Add(lblOffsetAPreview, 8, 1);
            gridPositioning.Controls.Add(pnlColorA, 9, 1);

            // Row 2: Inset F2 inputs
            gridPositioning.Controls.Add(btnAlignD, 1, 2);

            var lblInsetF2 = new Label { Text = "Inset F2:", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(0) };
            numOffsetB = new NumericUpDown { Minimum = -1000m, Maximum = 1000m, Value = 0m, Width = 64, Anchor = AnchorStyles.None, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0) };
            btnResetOffsetB = new Button { Text = "↺", Width = 22, Height = inputHeight, Margin = new Padding(0), FlatStyle = FlatStyle.System, Anchor = AnchorStyles.None };
            btnUsePercentageOffsetB = new Button { Text = "units", Width = 42, Height = inputHeight, Margin = new Padding(0), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.None };
            lblOffsetBPreview = new Label { Text = "", ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(2, 0, 0, 0) };
            var pnlColorB = new Panel { BackColor = Operations.BrushBuilderColors.Face2, Width = 12, Height = 12, Anchor = AnchorStyles.None, Margin = new Padding(0) };

            btnResetOffsetB.Click += (s, e) => { numOffsetB.Value = 0m; UpdateOffsetBPreview(); _tool.InvalidateViewports(); };
            btnUsePercentageOffsetB.Click += (s, e) => {
                _usePercentageOffsetB = !_usePercentageOffsetB;
                OnUnitToggleChanged(btnUsePercentageOffsetB, _usePercentageOffsetB, numOffsetB, -100m, 100m, -1000m, 1000m);
                UpdateOffsetBPreview();
            };
            numOffsetB.ValueChanged += (s, e) => { UpdateOffsetBPreview(); UpdateSettingsCache(); _tool.InvalidateViewports(); };

            gridPositioning.Controls.Add(lblInsetF2, 4, 2);
            gridPositioning.Controls.Add(numOffsetB, 5, 2);
            gridPositioning.Controls.Add(btnResetOffsetB, 6, 2);
            gridPositioning.Controls.Add(btnUsePercentageOffsetB, 7, 2);
            gridPositioning.Controls.Add(lblOffsetBPreview, 8, 2);
            gridPositioning.Controls.Add(pnlColorB, 9, 2);

            // Row 3: Depth Alignment
            var pnlDepthContainer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            var lblDepthAlign = new Label { Text = "Depth Alignment:", Font = new Font(this.Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            btnDepthF1 = new Button { Text = "F1", Width = 40, Height = 24, Margin = new Padding(1) };
            btnDepthMid = new Button { Text = "Mid", Width = 45, Height = 24, Margin = new Padding(1) };
            btnDepthF2 = new Button { Text = "F2", Width = 40, Height = 24, Margin = new Padding(1) };

            btnDepthF1.Click += (s, e) => SetDepth("F1");
            btnDepthMid.Click += (s, e) => SetDepth("Mid");
            btnDepthF2.Click += (s, e) => SetDepth("F2");
            pnlDepthContainer.Controls.AddRange(new Control[] { lblDepthAlign, btnDepthF1, btnDepthMid, btnDepthF2 });

            gridPositioning.Controls.Add(pnlDepthContainer, 4, 3);
            gridPositioning.SetColumnSpan(pnlDepthContainer, 6);

            grpThickness.Controls.Add(gridPositioning);
            pnlLeft.Controls.Add(grpThickness, 0, 3);

            // 4. Triangulation & Splitting GroupBox
            var grpTriangulation = new GroupBox { Text = "Triangulation & Splitting", Dock = DockStyle.Fill, Margin = new Padding(2) };
            var gridTriangulation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Padding = new Padding(2) };
            gridTriangulation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));  // "Method:" label
            gridTriangulation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // cmbSplitMode dropdown
            gridTriangulation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50f));  // "Slices:" label
            gridTriangulation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40f));  // numSlices numeric box
            gridTriangulation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22f));  // Reset slices button

            gridTriangulation.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            gridTriangulation.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

            chkTriangulate = new CheckBox { Text = "Triangulate non-planar", Checked = false, AutoSize = true, Margin = new Padding(2, 2, 0, 0) };
            gridTriangulation.Controls.Add(chkTriangulate, 0, 0);
            gridTriangulation.SetColumnSpan(chkTriangulate, 2);

            var lblSlices = new Label { Text = "Slices:", Anchor = AnchorStyles.Right, AutoSize = true, Margin = new Padding(0, 0, 4, 0) };
            gridTriangulation.Controls.Add(lblSlices, 2, 0);
            
            numSlices = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 1, Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0) };
            gridTriangulation.Controls.Add(numSlices, 3, 0);

            var btnResetSlices = new Button { Text = "↺", Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0), FlatStyle = FlatStyle.System };
            btnResetSlices.Click += (s, e) => {
                numSlices.Value = 1m;
            };
            gridTriangulation.Controls.Add(btnResetSlices, 4, 0);
            
            var lblSplitMode = new Label { Text = "Method:", Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(2, 0, 0, 0) };
            gridTriangulation.Controls.Add(lblSplitMode, 0, 1);
            
            cmbSplitMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 2, 0) };
            cmbSplitMode.Items.AddRange(new object[] { "One Solid (Convex)", "One Solid (Diag /)", "One Solid (Diag \\)", "Wedges (Radial)", "Tetrahedral" });
            cmbSplitMode.SelectedIndex = 0;
            cmbSplitMode.Enabled = false;
            gridTriangulation.Controls.Add(cmbSplitMode, 1, 1);
            gridTriangulation.SetColumnSpan(cmbSplitMode, 4);

            chkTriangulate.CheckedChanged += (s, e) => {
                cmbSplitMode.Enabled = chkTriangulate.Checked;
                UpdateSettingsCache();
                _tool.InvalidateViewports();
            };
            cmbSplitMode.SelectedIndexChanged += (s, e) => {
                UpdateSettingsCache();
                _tool.InvalidateViewports();
            };
            numSlices.ValueChanged += (s, e) => {
                UpdateSettingsCache();
                _tool.InvalidateViewports();
            };

            grpTriangulation.Controls.Add(gridTriangulation);
            pnlLeft.Controls.Add(grpTriangulation, 0, 2);



            // Right Column layout (Faces list, Instructions)
            var pnlRight = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(4)
            };
            var grpFaces = new GroupBox { Text = "Faces Selection", Dock = DockStyle.Fill, Margin = new Padding(2) };
            var pnlFacesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            pnlFacesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));   // Swap button
            pnlFacesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // ListBox
            pnlFacesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));   // Help text label

            btnSwap = new Button { Text = "Swap F1 ↔ F2 Anchor Roles", Dock = DockStyle.Fill, Height = 26 };
            btnSwap.Click += (s, e) => {
                if (_tool.SelectedFaces.Count >= 2)
                {
                    var temp = _tool.SelectedFaces[0];
                    _tool.SelectedFaces[0] = _tool.SelectedFaces[1];
                    _tool.SelectedFaces[1] = temp;
                    UpdateSelectionStatus();
                    _tool.InvalidateViewports();
                }
            };

            lstFaces = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.None, Font = new Font("Segoe UI", 9f) };

            lblInstruction = new Label
            {
                Text = "Click = Face 1 (Anchor)  ·  Ctrl+Click = add\nFace 3+ = ✂ clip  ·  Click empty space = clear",
                Dock = DockStyle.Fill,
                ForeColor = SystemColors.GrayText,
                Font = new Font(this.Font.FontFamily, 8f),
                TextAlign = ContentAlignment.TopLeft
            };

            pnlFacesLayout.Controls.Add(btnSwap, 0, 0);
            pnlFacesLayout.Controls.Add(lstFaces, 0, 1);
            pnlFacesLayout.Controls.Add(lblInstruction, 0, 2);
            grpFaces.Controls.Add(pnlFacesLayout);
            pnlRight.Controls.Add(grpFaces, 0, 0);

            // Tab Control structure
            var tabControl = new TabControl { Dock = DockStyle.Fill, Alignment = TabAlignment.Bottom };
            var tabPageBuilder = new TabPage { Text = "Builder" };
            var tabPageLog = new TabPage { Text = "Execution Log" };
            tabControl.TabPages.Add(tabPageBuilder);
            tabControl.TabPages.Add(tabPageLog);

            // Main Builder tab container panel
            var pnlBuilderMain = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            pnlBuilderMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f)); // Left settings
            pnlBuilderMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f)); // Right faces list

            pnlBuilderMain.Controls.Add(pnlLeft, 0, 0);
            pnlBuilderMain.Controls.Add(pnlRight, 1, 0);

            // Log Tab Setup
            var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.25f)
            };
            var btnClearLog = new Button { Text = "Clear Log", Height = 25, Dock = DockStyle.Fill };
            btnClearLog.Click += (s, e) => txtLog.Clear();
            logLayout.Controls.Add(txtLog, 0, 0);
            logLayout.Controls.Add(btnClearLog, 0, 1);
            tabPageLog.Controls.Add(logLayout);

            // Bottom Presets Panel
            var bottomBar = new Panel { Dock = DockStyle.Fill, Height = 48, Padding = new Padding(8, 6, 8, 6) };
            var presetsPanel = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
            cmbProfiles = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            
            var btnAddProfile    = new Button { Text = "Add", Width = 55, Height = 24 };
            var btnCloneProfile  = new Button { Text = "Clone", Width = 55, Height = 24 };
            var btnRenameProfile = new Button { Text = "Rename", Width = 65, Height = 24 };
            var btnDeleteProfile = new Button { Text = "Delete", Width = 55, Height = 24 };
            var lblPreset = new Label { Text = "Preset:", AutoSize = true, Margin = new Padding(0, 4, 4, 0) };

            presetsPanel.Controls.AddRange(new Control[] {
                lblPreset, cmbProfiles, btnAddProfile, btnCloneProfile, btnRenameProfile, btnDeleteProfile
            });
            bottomBar.Controls.Add(presetsPanel);

            btnAddProfile.Click += (s, e) => AddCurrentProfile();
            btnCloneProfile.Click += (s, e) => CloneCurrentProfile();
            btnDeleteProfile.Click += (s, e) => DeleteCurrentProfile();
            btnRenameProfile.Click += (s, e) => RenameCurrentProfile();

            // Unified non-overlapping container for builder content and bottom preset bar (eliminates Dock Z-order bugs)
            var pnlBuilderContainer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            pnlBuilderContainer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            pnlBuilderContainer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

            pnlBuilderMain.Dock = DockStyle.Fill;
            bottomBar.Dock = DockStyle.Fill;
            pnlBuilderContainer.Controls.Add(pnlBuilderMain, 0, 0);
            pnlBuilderContainer.Controls.Add(bottomBar, 0, 1);

            tabPageBuilder.Controls.Add(pnlBuilderContainer);
            this.Controls.Add(tabControl);

            // Wire up main actions
            btnBuild.Click += async (s, e) => await PerformBuild();

            UpdateAlignmentButtons();
            UpdateDepthButtons();
            UpdateSizeModeButtons();
            UpdateCopySideButtons();
            UpdateThicknessPreview();
            UpdateOffsetAPreview();
            UpdateOffsetBPreview();
            UpdateUnitToggleButtonsColors();
            SetupUndoOnEmpty(numShiftOffset);
            SetupUndoOnEmpty(numThickness);
            SetupUndoOnEmpty(numOffsetA);
            SetupUndoOnEmpty(numOffsetB);

            UpdateSettingsCache();
        }

        private void OnUnitToggleChanged(Button btn, bool usePct, NumericUpDown num, decimal pctMin, decimal pctMax, decimal unitMin, decimal unitMax)
        {
            btn.Text = usePct ? "%" : "units";
            num.Minimum = usePct ? pctMin : unitMin;
            num.Maximum = usePct ? pctMax : unitMax;
            num.Value = Math.Clamp(num.Value, num.Minimum, num.Maximum);
            num.Increment = usePct ? 5m : 1m;
            UpdateUnitToggleButtonsColors();
            UpdateSettingsCache();
            _tool.InvalidateViewports();
        }

        private void SetupUndoOnEmpty(NumericUpDown num)
        {
            decimal lastValidValue = num.Value;
            num.Enter += (s, e) => {
                lastValidValue = num.Value;
            };
            num.ValueChanged += (s, e) => {
                if (!string.IsNullOrEmpty(num.Text))
                {
                    lastValidValue = num.Value;
                }
            };

            void RestoreIfEmpty()
            {
                if (string.IsNullOrEmpty(num.Text))
                {
                    num.Value = lastValidValue;
                    num.Text = lastValidValue.ToString();
                }
            }

            num.Leave += (s, e) => RestoreIfEmpty();
            num.MouseLeave += (s, e) => RestoreIfEmpty();
        }

        private void UpdateUnitToggleButtonsColors()
        {
            var toggles = new[] {
                (btnUsePercentageThick, _usePercentageThick),
                (btnUsePercentageOffsetA, _usePercentageOffsetA),
                (btnUsePercentageOffsetB, _usePercentageOffsetB)
            };

            foreach (var (btn, active) in toggles)
            {
                if (btn == null) continue;
                if (active)
                {
                    btn.BackColor = Color.Orange;
                    btn.ForeColor = Color.Black;
                    btn.FlatAppearance.BorderColor = Color.DarkOrange;
                }
                else
                {
                    btn.BackColor = SystemColors.Control;
                    btn.ForeColor = SystemColors.ControlText;
                    btn.FlatAppearance.BorderColor = SystemColors.ControlDark;
                }
            }
        }

        private void SetSizeMode(string mode)
        {
            _selectedSizeMode = mode;
            UpdateSizeModeButtons();
            UpdateProfileShapeTip();
            UpdateSettingsCache();
            _tool.InvalidateViewports();
        }

        private void UpdateSizeModeButtons()
        {
            var buttons = new[] {
                (btnModeLoft, "Stretch (Loft)"),
                (btnModeF1, "Use Face 1 (Blue)"),
                (btnModeF2, "Use Face 2 (Green)"),
                (btnModeMin, "Smaller Face"),
                (btnModeMax, "Larger Face")
            };

            Color normalBack = btnBuild != null ? btnBuild.BackColor : SystemColors.Control;
            Color normalFore = btnBuild != null ? btnBuild.ForeColor : SystemColors.ControlText;

            foreach (var (btn, modeVal) in buttons)
            {
                if (btn == null) continue;
                bool isActive = _selectedSizeMode.StartsWith(modeVal, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    btn.BackColor = Color.DodgerBlue;
                    btn.ForeColor = Color.White;
                    btn.Font = new Font(btn.Font, FontStyle.Bold);
                }
                else
                {
                    btn.BackColor = normalBack;
                    btn.ForeColor = normalFore;
                    btn.Font = new Font(btn.Font, FontStyle.Regular);
                }
            }
        }

        private void SetCopySide(string side)
        {
            _selectedCopySide = side;
            UpdateCopySideButtons();
            UpdateSettingsCache();
            _tool.InvalidateViewports();
        }

        private void UpdateCopySideButtons()
        {
            if (copyRoseButtons == null) return;

            Color normalBack = btnBuild != null ? btnBuild.BackColor : SystemColors.Control;
            Color normalFore = btnBuild != null ? btnBuild.ForeColor : SystemColors.ControlText;

            for (int i = 0; i < 9; i++)
            {
                var btn = copyRoseButtons[i];
                if (btn == null) continue;

                string sideVal = "None";
                if (i == 1) sideVal = "U";
                else if (i == 3) sideVal = "L";
                else if (i == 5) sideVal = "R";
                else if (i == 7) sideVal = "D";

                bool isActive = _selectedCopySide.Equals(sideVal, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    btn.BackColor = Color.DodgerBlue;
                    btn.ForeColor = Color.White;
                    btn.Font = new Font(btn.Font, FontStyle.Bold);
                }
                else
                {
                    btn.BackColor = normalBack;
                    btn.ForeColor = normalFore;
                    btn.Font = new Font(btn.Font, FontStyle.Regular);
                }
            }
        }

        private void SetAlignment(string align)
        {
            _selectedAlignment = align;
            UpdateAlignmentButtons();
            UpdateSettingsCache();
            _tool.InvalidateViewports();
        }

        private void UpdateAlignmentButtons()
        {
            var buttons = new[] { btnAlignU, btnAlignD, btnAlignL, btnAlignR, btnAlignC };
            Color normalBack = btnBuild != null ? btnBuild.BackColor : SystemColors.Control;
            Color normalFore = btnBuild != null ? btnBuild.ForeColor : SystemColors.ControlText;

            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                string btnTag = btn.Tag as string ?? "";
                bool isActive = btnTag.Equals(_selectedAlignment, StringComparison.OrdinalIgnoreCase);

                if (isActive)
                {
                    btn.BackColor = Color.DodgerBlue;
                    btn.ForeColor = Color.White;
                    btn.Font = new Font(btn.Font, FontStyle.Bold);
                }
                else
                {
                    btn.BackColor = normalBack;
                    btn.ForeColor = normalFore;
                    btn.Font = new Font(btn.Font, FontStyle.Regular);
                }
            }
        }

        private void SetDepth(string depthVal)
        {
            _selectedDepth = depthVal;
            UpdateDepthButtons();
            UpdateSettingsCache();
            _tool.InvalidateViewports();
        }

        private void UpdateDepthButtons()
        {
            var buttons = new[] { btnDepthF1, btnDepthMid, btnDepthF2 };
            Color normalBack = btnBuild != null ? btnBuild.BackColor : SystemColors.Control;
            Color normalFore = btnBuild != null ? btnBuild.ForeColor : SystemColors.ControlText;

            foreach (var btn in buttons)
            {
                if (btn == null) continue;
                bool isActive = btn.Text.Equals(_selectedDepth, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    btn.BackColor = Color.DodgerBlue;
                    btn.ForeColor = Color.White;
                    btn.Font = new Font(btn.Font, FontStyle.Bold);
                }
                else
                {
                    btn.BackColor = normalBack;
                    btn.ForeColor = normalFore;
                    btn.Font = new Font(btn.Font, FontStyle.Regular);
                }
            }
        }

        private void UpdateProfileShapeTip()
        {
            if (lblProfileShapeTip == null) return;

            string sizeMode = _selectedSizeMode;

            if (sizeMode.StartsWith("Stretch", StringComparison.OrdinalIgnoreCase))
            {
                lblProfileShapeTip.Text = "Shape is stretched/interpolated between Face 1 and Face 2.";
                lblProfileShapeTip.ForeColor = SystemColors.GrayText;
            }
            else if (sizeMode.StartsWith("Use Face 1", StringComparison.OrdinalIgnoreCase))
            {
                lblProfileShapeTip.Text = "✓ Shape is preserved from Face 1 (Blue).";
                lblProfileShapeTip.ForeColor = SystemColors.GrayText;
            }
            else if (sizeMode.StartsWith("Use Face 2", StringComparison.OrdinalIgnoreCase))
            {
                lblProfileShapeTip.Text = "✓ Shape is preserved from Face 2 (Green).";
                lblProfileShapeTip.ForeColor = SystemColors.GrayText;
            }
            else if (sizeMode.StartsWith("Smaller", StringComparison.OrdinalIgnoreCase))
            {
                lblProfileShapeTip.Text = "✓ Shape is preserved from the smaller face.";
                lblProfileShapeTip.ForeColor = SystemColors.GrayText;
            }
            else if (sizeMode.StartsWith("Larger", StringComparison.OrdinalIgnoreCase))
            {
                lblProfileShapeTip.Text = "✓ Shape is preserved from the larger face.";
                lblProfileShapeTip.ForeColor = SystemColors.GrayText;
            }
            else
            {
                lblProfileShapeTip.Text = "";
            }
        }



        public void AppendLog(string message)
        {
            if (txtLog == null) return;
            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(new Action(() => AppendLog(message)));
            }
            else
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }

        private void UpdateSelectionStatus()
        {
            var currentIds = _tool.SelectedFaces.Select(x => (long)x.Face.ID).ToList();
            bool setChanged = currentIds.Count != _originalFaceIds.Count || 
                             !currentIds.All(_originalFaceIds.Contains);

            if (setChanged)
            {
                _originalFaceIds.Clear();
                _originalFaceIds.AddRange(currentIds);
            }

            bool selectionChanged = false;
            if (_tool.SelectedFaces.Count != _cachedFaceIds.Count)
            {
                selectionChanged = true;
            }
            else
            {
                for (int i = 0; i < _tool.SelectedFaces.Count; i++)
                {
                    if (_tool.SelectedFaces[i].Face.ID != _cachedFaceIds[i])
                    {
                        selectionChanged = true;
                        break;
                    }
                }
            }

            if (selectionChanged)
            {
                _cachedFaceIds.Clear();
                _cachedFaceIds.AddRange(_tool.SelectedFaces.Select(x => (long)x.Face.ID));

                numShiftOffset.Value = 0;
                _tool.AlignmentShiftOffset = 0;
            }

            if ((selectionChanged || setChanged) && lstFaces != null)
            {
                lstFaces.BeginUpdate();
                lstFaces.Items.Clear();
                for (int i = 0; i < _tool.SelectedFaces.Count; i++)
                {
                    var entry = _tool.SelectedFaces[i];
                    string prefix = i == 0 ? "F1 (Anchor)" : (i == 1 ? "F2 (Loft)" : $"F{i+1} ✂ clip");
                    string colSign = i == 0 ? "[Blue] " : (i == 1 ? "[Green] " : "[Orange] ");
                    lstFaces.Items.Add($"{colSign}{prefix} (ID: {entry.Face.ID}, Verts: {entry.Face.Vertices.Count})");
                }

                if (_tool.SelectedFaces.Count == 0)
                {
                    lstFaces.Items.Add("No faces selected in viewports.");
                }
                lstFaces.EndUpdate();
            }

            bool isSwapped = false;
            if (_tool.SelectedFaces.Count >= 2 && _originalFaceIds.Count >= 2)
            {
                if (_tool.SelectedFaces[0].Face.ID == _originalFaceIds[1] && _tool.SelectedFaces[1].Face.ID == _originalFaceIds[0])
                {
                    isSwapped = true;
                }
            }

            if (btnSwap != null)
            {
                btnSwap.Enabled = _tool.SelectedFaces.Count >= 2;
                if (isSwapped)
                {
                    btnSwap.Text = "F2 ↔ F1 [Swapped]";
                    btnSwap.BackColor = Color.Orange;
                    btnSwap.ForeColor = Color.Black;
                    btnSwap.Font = new Font(btnSwap.Font, FontStyle.Bold);
                }
                else
                {
                    btnSwap.Text = "Swap F1 ↔ F2 Anchor Roles";
                    btnSwap.BackColor = btnBuild != null ? btnBuild.BackColor : SystemColors.Control;
                    btnSwap.ForeColor = btnBuild != null ? btnBuild.ForeColor : SystemColors.ControlText;
                    btnSwap.Font = new Font(btnSwap.Font, FontStyle.Regular);
                }
            }

            if (_tool.SelectedFaces.Count >= 2)
            {
                var fA = _tool.SelectedFaces[0].Face;
                var fB = _tool.SelectedFaces[1].Face;
                if (fA.Vertices.Count == fB.Vertices.Count && fA.Vertices.Count >= 3)
                {
                    numShiftOffset.Maximum = fA.Vertices.Count - 1;
                    numShiftOffset.Enabled = true;
                    btnResetShiftOffset.Enabled = true;
                }
                else
                {
                    numShiftOffset.Maximum = 0;
                    numShiftOffset.Enabled = false;
                    btnResetShiftOffset.Enabled = false;
                }
            }
            else
            {
                numShiftOffset.Maximum = 0;
                numShiftOffset.Enabled = false;
                btnResetShiftOffset.Enabled = false;
            }

            UpdateThicknessPreview();
            UpdateOffsetAPreview();
            UpdateOffsetBPreview();

            if (btnBuild != null) btnBuild.Enabled = !_isBuilding && _tool.SelectedFaces.Count >= 2;
        }

        private float GetLoftSpan()
        {
            if (_tool.SelectedFaces.Count >= 2)
            {
                var originA = _tool.SelectedFaces[0].Face.Origin;
                var originB = _tool.SelectedFaces[1].Face.Origin;
                return (originB - originA).Length();
            }
            return 0f;
        }

        private void UpdateThicknessPreview()
        {
            if (lblThicknessPreview == null) return;
            float span = GetLoftSpan();
            if (span >= 1e-3f)
            {
                if (_usePercentageThick)
                {
                    decimal val = (numThickness.Value / 100m) * (decimal)span;
                    lblThicknessPreview.Text = $" (~{val:F1}u)";
                }
                else
                {
                    decimal pct = (numThickness.Value / (decimal)span) * 100m;
                    lblThicknessPreview.Text = $" (~{pct:F1}%)";
                }
            }
            else
            {
                lblThicknessPreview.Text = "";
            }
        }

        private void UpdateOffsetAPreview()
        {
            if (lblOffsetAPreview == null) return;
            float span = GetLoftSpan();
            if (span >= 1e-3f)
            {
                if (_usePercentageOffsetA)
                {
                    decimal val = (numOffsetA.Value / 100m) * (decimal)span;
                    lblOffsetAPreview.Text = $" (~{val:F1}u)";
                }
                else
                {
                    decimal pct = (numOffsetA.Value / (decimal)span) * 100m;
                    lblOffsetAPreview.Text = $" (~{pct:F1}%)";
                }
            }
            else
            {
                lblOffsetAPreview.Text = "";
            }
        }

        private void UpdateOffsetBPreview()
        {
            if (lblOffsetBPreview == null) return;
            float span = GetLoftSpan();
            if (span >= 1e-3f)
            {
                if (_usePercentageOffsetB)
                {
                    decimal val = (numOffsetB.Value / 100m) * (decimal)span;
                    lblOffsetBPreview.Text = $" (~{val:F1}u)";
                }
                else
                {
                    decimal pct = (numOffsetB.Value / (decimal)span) * 100m;
                    lblOffsetBPreview.Text = $" (~{pct:F1}%)";
                }
            }
            else
            {
                lblOffsetBPreview.Text = "";
            }
        }

        private async Task PerformBuild()
        {
            if (_isBuilding) return;
            _isBuilding = true;
            UpdateSelectionStatus();

            try
            {
                var doc = _tool.GetDocument();
                if (doc == null || _tool.SelectedFaces.Count < 2) return;

                string sizeMode = _selectedSizeMode;
                string alignment = _selectedAlignment;
                string depth = _selectedDepth;
                int validationMode = (_settings.Value?.Validation?.Equals("Warn Only", StringComparison.OrdinalIgnoreCase) == true) ? 1 
                                   : ((_settings.Value?.Validation?.Equals("Ignore Warnings", StringComparison.OrdinalIgnoreCase) == true) ? 2 : 0);
                float thickness = (float)numThickness.Value;
                bool usePercentageThick = _usePercentageThick;

                float offsetA = (float)numOffsetA.Value;
                bool usePercentageOffsetA = _usePercentageOffsetA;
                float offsetB = (float)numOffsetB.Value;
                bool usePercentageOffsetB = _usePercentageOffsetB;

                Operations.BuildBrushOperation.OnLog = AppendLog;

                var face1 = _tool.SelectedFaces[0].Face;
                var solid1 = _tool.SelectedFaces[0].Solid;
                var otherFaces = _tool.SelectedFaces.Skip(1).Select(x => x.Face).ToList();
                var otherHitPoints = _tool.SelectedFaces.Skip(1).Select(x => x.HitPoint).ToList();

                string copySide = SelectedCopySide;

                var op = Operations.BuildBrushOperation.Create(
                    doc,
                    face1, solid1,
                    otherFaces,
                    otherHitPoints,
                    sizeMode,
                    alignment,
                    depth,
                    offsetA, offsetB,
                    usePercentageOffsetA, usePercentageOffsetB,
                    thickness, usePercentageThick,
                    copySide,
                    validationMode,
                    Triangulate,
                    SplitMode,
                    SelectedSlices,
                    _tool.AlignmentShiftOffset
                );

                if (op != null)
                {
                    await MapDocumentOperation.Perform(doc, op);
                    AppendLog("Brush built successfully.");
                }
                else
                {
                    AppendLog("Brush build operation returned null.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error building brush: {ex.Message}");
                MessageBox.Show($"Error building brush: {ex.Message}", "Brush Builder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isBuilding = false;
                UpdateSelectionStatus();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Oy.Publish("ActivateTool", "SelectTool");
            }
            else
            {
                _tool.SelectionChanged -= OnSelectionChanged;
            }
            base.OnFormClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var parent = this.Owner;
            if (parent != null)
            {
                bool isDark = parent.BackColor.R < 100;
                Sledge.Shell.Registers.DialogRegister.ColorControlsRecursively(this, isDark);
            }
            LoadSettings();
            UpdateSelectionStatus();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!this.Visible)
            {
                SaveSettings();
            }
        }

        private static string GetSettingsPath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hammertime");
            return Path.Combine(folder, "BrushBuilderSettings.json");
        }

        private void LoadSettings()
        {
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                var settings = System.Text.Json.JsonSerializer.Deserialize<BrushBuilderSettings>(json);
                if (settings == null) return;

                if (settings.WindowX != int.MinValue && settings.WindowY != int.MinValue)
                {
                    var rect = new Rectangle(settings.WindowX, settings.WindowY, this.Width, this.Height);
                    if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
                    {
                        this.Location = new Point(settings.WindowX, settings.WindowY);
                    }
                }

                _selectedSizeMode = settings.SizeMode ?? "Stretch (Loft)";
                _selectedAlignment = settings.Alignment ?? "C";
                _selectedDepth = settings.Depth ?? "Mid";
                _selectedCopySide = settings.CopySide ?? "None";

                _usePercentageThick = settings.UsePercentageThick;
                _usePercentageOffsetA = settings.UsePercentageOffsetA;
                _usePercentageOffsetB = settings.UsePercentageOffsetB;

                numOffsetA.Value = Math.Clamp(settings.OffsetA, numOffsetA.Minimum, numOffsetA.Maximum);
                numOffsetB.Value = Math.Clamp(settings.OffsetB, numOffsetB.Minimum, numOffsetB.Maximum);
                numThickness.Value = Math.Clamp(settings.Thickness, numThickness.Minimum, numThickness.Maximum);

                UpdateAlignmentButtons();
                UpdateDepthButtons();
                UpdateSizeModeButtons();
                UpdateCopySideButtons();
                UpdateThicknessPreview();
                UpdateOffsetAPreview();
                UpdateOffsetBPreview();
                UpdateUnitToggleButtonsColors();
                UpdateProfileShapeTip();

                chkTriangulate.Checked = settings.Triangulate;
                cmbSplitMode.SelectedItem = settings.SplitMode ?? "One Solid (Convex)";
                cmbSplitMode.Enabled = settings.Triangulate;
                numSlices.Value = Math.Clamp(settings.Slices, 1, 20);
                UpdateSettingsCache();
            }
            catch
            {
                // Ignore load settings errors
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new BrushBuilderSettings
                {
                    WindowX = this.Location.X,
                    WindowY = this.Location.Y,
                    SizeMode = _selectedSizeMode,
                    Alignment = _selectedAlignment,
                    Depth = _selectedDepth,
                    CopySide = _selectedCopySide,
                    OffsetA = numOffsetA.Value,
                    UsePercentageOffsetA = _usePercentageOffsetA,
                    OffsetB = numOffsetB.Value,
                    UsePercentageOffsetB = _usePercentageOffsetB,
                    Thickness = numThickness.Value,
                    UsePercentageThick = _usePercentageThick,
                    Triangulate = chkTriangulate.Checked,
                    SplitMode = cmbSplitMode.SelectedItem?.ToString() ?? "One Solid (Convex)",
                    Slices = (int)numSlices.Value
                };

                var path = GetSettingsPath();
                var folder = Path.GetDirectoryName(path);
                if (folder != null && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore save settings errors
            }
        }

        private static readonly string ProfilesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Hammertime",
            "BrushBuilderProfiles.json"
        );

        private void LoadProfilesList()
        {
            _profiles.Clear();
            try
            {
                if (File.Exists(ProfilesFilePath))
                {
                    string json = File.ReadAllText(ProfilesFilePath);
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<List<ToolProfile>>(json);
                    if (loaded != null) _profiles.AddRange(loaded);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to load profiles: {ex.Message}");
            }

            if (_profiles.Count == 0)
            {
                _profiles.Add(new ToolProfile { Name = "Default", SizeMode = "Stretch (Loft)", Thickness = 0 });
                _profiles.Add(new ToolProfile { Name = "Window Pane (Thin)", SizeMode = "Stretch (Loft)", Thickness = 4, Alignment = "C", Depth = "Mid" });
                _profiles.Add(new ToolProfile { Name = "Along-Span (16 units)", SizeMode = "Stretch (Loft)", Thickness = 16, Alignment = "C", Depth = "F1" });
                SaveProfilesList();
            }

            UpdateProfilesCombo();
        }

        private void SaveProfilesList()
        {
            try
            {
                string? dir = Path.GetDirectoryName(ProfilesFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = System.Text.Json.JsonSerializer.Serialize(_profiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ProfilesFilePath, json);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to save profiles: {ex.Message}");
            }
        }

        private void UpdateProfilesCombo()
        {
            if (cmbProfiles == null) return;
            
            cmbProfiles.SelectedIndexChanged -= CmbProfiles_SelectedIndexChanged;
            
            string? currentName = cmbProfiles.SelectedItem?.ToString();
            cmbProfiles.Items.Clear();
            foreach (var p in _profiles)
            {
                cmbProfiles.Items.Add(p.Name);
            }
            
            int idx = -1;
            if (currentName != null)
            {
                idx = cmbProfiles.FindStringExact(currentName);
            }
            if (idx == -1 && cmbProfiles.Items.Count > 0)
            {
                idx = 0;
            }
            cmbProfiles.SelectedIndex = idx;
            
            cmbProfiles.SelectedIndexChanged += CmbProfiles_SelectedIndexChanged;
            
            if (idx >= 0 && idx < _profiles.Count)
            {
                LoadProfile(_profiles[idx]);
            }
        }

        private void CmbProfiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            int idx = cmbProfiles.SelectedIndex;
            if (idx >= 0 && idx < _profiles.Count)
            {
                LoadProfile(_profiles[idx]);
            }
        }

        private void LoadProfile(ToolProfile profile)
        {
            if (profile == null) return;
            
            _selectedSizeMode = profile.SizeMode;
            _selectedAlignment = profile.Alignment;
            _selectedDepth = profile.Depth;
            
            _usePercentageThick = profile.UsePercentageThick;
            numThickness.Value = (decimal)profile.Thickness;
            
            numOffsetA.Value = (decimal)profile.OffsetA;
            _usePercentageOffsetA = profile.UsePercentageOffsetA;
            numOffsetB.Value = (decimal)profile.OffsetB;
            _usePercentageOffsetB = profile.UsePercentageOffsetB;
            
            if (numShiftOffset.Enabled)
            {
                numShiftOffset.Value = Math.Clamp(profile.RotationOffset, (int)numShiftOffset.Minimum, (int)numShiftOffset.Maximum);
            }
            
            _selectedCopySide = profile.CopySide ?? "None";

            UpdateAlignmentButtons();
            UpdateDepthButtons();
            UpdateSizeModeButtons();
            UpdateCopySideButtons();
            UpdateThicknessPreview();
            UpdateOffsetAPreview();
            UpdateOffsetBPreview();
            UpdateUnitToggleButtonsColors();
            UpdateProfileShapeTip();

            chkTriangulate.Checked = profile.Triangulate;
            cmbSplitMode.SelectedItem = profile.SplitMode ?? "One Solid (Convex)";
            cmbSplitMode.Enabled = profile.Triangulate;
            numSlices.Value = Math.Clamp(profile.Slices > 0 ? profile.Slices : 1, 1, 20);
            UpdateSettingsCache();
        }

        private ToolProfile GetCurrentSettings(string name)
        {
            return new ToolProfile
            {
                Name = name,
                SizeMode = _selectedSizeMode,
                Alignment = _selectedAlignment,
                Depth = _selectedDepth,
                CopySide = _selectedCopySide,
                Thickness = (float)numThickness.Value,
                UsePercentageThick = _usePercentageThick,
                OffsetA = (float)numOffsetA.Value,
                UsePercentageOffsetA = _usePercentageOffsetA,
                OffsetB = (float)numOffsetB.Value,
                UsePercentageOffsetB = _usePercentageOffsetB,
                ValidationMode = (_settings.Value?.Validation?.Equals("Warn Only", StringComparison.OrdinalIgnoreCase) == true) ? 1 
                               : ((_settings.Value?.Validation?.Equals("Ignore Warnings", StringComparison.OrdinalIgnoreCase) == true) ? 2 : 0),
                RotationOffset = (int)numShiftOffset.Value,
                Triangulate = chkTriangulate.Checked,
                SplitMode = cmbSplitMode.SelectedItem?.ToString() ?? "One Solid (Convex)",
                Slices = (int)numSlices.Value
            };
        }

        private void ResetAllSettings()
        {
            _selectedSizeMode = "Stretch (Loft)";
            _selectedAlignment = "C";
            _selectedDepth = "Mid";
            _selectedCopySide = "None";
            numThickness.Value = 0m;
            _usePercentageThick = false;
            
            numOffsetA.Value = 0m;
            _usePercentageOffsetA = false;
            numOffsetB.Value = 0m;
            _usePercentageOffsetB = false;

            chkTriangulate.Checked = false;
            cmbSplitMode.SelectedIndex = 0;
            numSlices.Value = 1m;
            
            if (numShiftOffset.Enabled)
            {
                numShiftOffset.Value = 0m;
            }
            
            UpdateAlignmentButtons();
            UpdateDepthButtons();
            UpdateSizeModeButtons();
            UpdateCopySideButtons();
            UpdateThicknessPreview();
            UpdateOffsetAPreview();
            UpdateOffsetBPreview();
            UpdateUnitToggleButtonsColors();
            UpdateSettingsCache();
            _tool.InvalidateViewports();
        }

        private void AddCurrentProfile()
        {
            string name = PromptForInput("Enter profile name:", "Add Profile");
            if (string.IsNullOrWhiteSpace(name)) return;

            name = name.Trim();
            if (_profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A profile named '{name}' already exists.", "Add Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newProfile = GetCurrentSettings(name);
            _profiles.Add(newProfile);
            SaveProfilesList();
            UpdateProfilesCombo();
            
            int idx = cmbProfiles.FindStringExact(name);
            if (idx >= 0) cmbProfiles.SelectedIndex = idx;
        }

        private void CloneCurrentProfile()
        {
            int idx = cmbProfiles.SelectedIndex;
            if (idx < 0 || idx >= _profiles.Count) return;

            var source = _profiles[idx];
            string name = PromptForInput("Enter name for cloned profile:", "Clone Profile", source.Name + " Copy");
            if (string.IsNullOrWhiteSpace(name)) return;

            name = name.Trim();
            if (_profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A profile named '{name}' already exists.", "Clone Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var clone = GetCurrentSettings(name);
            clone.SizeMode = source.SizeMode;
            clone.Thickness = source.Thickness;
            clone.UsePercentageThick = source.UsePercentageThick;
            clone.Alignment = source.Alignment;
            clone.Depth = source.Depth;
            clone.OffsetA = source.OffsetA;
            clone.UsePercentageOffsetA = source.UsePercentageOffsetA;
            clone.OffsetB = source.OffsetB;
            clone.UsePercentageOffsetB = source.UsePercentageOffsetB;
            clone.RotationOffset = source.RotationOffset;
            clone.ValidationMode = source.ValidationMode;
            _profiles.Add(clone);
            SaveProfilesList();
            UpdateProfilesCombo();

            int newIdx = cmbProfiles.FindStringExact(name);
            if (newIdx >= 0) cmbProfiles.SelectedIndex = newIdx;
        }

        private void DeleteCurrentProfile()
        {
            int idx = cmbProfiles.SelectedIndex;
            if (idx < 0 || idx >= _profiles.Count) return;

            var profile = _profiles[idx];
            if (profile.Name == "Default")
            {
                MessageBox.Show("Cannot delete the Default profile.", "Delete Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show($"Are you sure you want to delete profile '{profile.Name}'?", "Delete Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm == DialogResult.Yes)
            {
                _profiles.RemoveAt(idx);
                SaveProfilesList();
                UpdateProfilesCombo();
            }
        }

        private void RenameCurrentProfile()
        {
            int idx = cmbProfiles.SelectedIndex;
            if (idx < 0 || idx >= _profiles.Count) return;

            var profile = _profiles[idx];
            if (profile.Name == "Default")
            {
                MessageBox.Show("Cannot rename the Default profile.", "Rename Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string newName = PromptForInput("Enter new profile name:", "Rename Profile", profile.Name);
            if (string.IsNullOrWhiteSpace(newName)) return;

            newName = newName.Trim();
            if (newName.Equals(profile.Name, StringComparison.Ordinal)) return;

            if (_profiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A profile named '{newName}' already exists.", "Rename Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            profile.Name = newName;
            SaveProfilesList();
            UpdateProfilesCombo();

            int newIdx = cmbProfiles.FindStringExact(newName);
            if (newIdx >= 0) cmbProfiles.SelectedIndex = newIdx;
        }

        private string PromptForInput(string text, string caption, string defaultValue = "")
        {
            Form prompt = new Form()
            {
                Width = 300,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };
            Label textLabel = new Label() { Left = 20, Top = 20, Text = text, Width = 260 };
            TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 240, Text = defaultValue };
            Button confirmation = new Button() { Text = "OK", Left = 100, Width = 80, Top = 80, DialogResult = DialogResult.OK };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;
            return prompt.ShowDialog(this) == DialogResult.OK ? textBox.Text : "";
        }

        public string SelectedSizeMode { get; private set; } = "Stretch (Loft)";
        public string SelectedAlignment { get; private set; } = "C";
        public string SelectedDepth { get; private set; } = "Mid";
        public float SelectedOffsetA { get; private set; } = 0f;
        public bool SelectedUsePercentageOffsetA { get; private set; } = false;
        public float SelectedOffsetB { get; private set; } = 0f;
        public bool SelectedUsePercentageOffsetB { get; private set; } = false;
        public float SelectedThickness { get; private set; } = 0f;
        public bool SelectedUsePercentageThick { get; private set; } = false;
        public string SelectedCopySide { get; private set; } = "None";
        public bool Triangulate { get; private set; } = false;
        public string SplitMode { get; private set; } = "One Solid (Convex)";
        public int SelectedSlices { get; private set; } = 1;

        public void UpdateSettingsCache()
        {
            SelectedSizeMode = _selectedSizeMode;
            SelectedAlignment = _selectedAlignment;
            SelectedDepth = _selectedDepth;
            SelectedOffsetA = (float)numOffsetA.Value;
            SelectedUsePercentageOffsetA = _usePercentageOffsetA;
            SelectedOffsetB = (float)numOffsetB.Value;
            SelectedUsePercentageOffsetB = _usePercentageOffsetB;
            SelectedThickness = (float)numThickness.Value;
            SelectedUsePercentageThick = _usePercentageThick;
            SelectedCopySide = _selectedCopySide;
            Triangulate = chkTriangulate != null && chkTriangulate.Checked;
            SplitMode = cmbSplitMode?.SelectedItem?.ToString() ?? "One Solid (Convex)";
            SelectedSlices = numSlices != null ? (int)numSlices.Value : 1;
        }

        private static TableLayoutPanel WrapRoseGrid(Button[] buttons)
        {
            var grid = new TableLayoutPanel { Width = 84, Height = 84, Anchor = AnchorStyles.Left | AnchorStyles.Top, ColumnCount = 3, RowCount = 3, Margin = new Padding(4, 2, 4, 0) };
            for (int c = 0; c < 3; c++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            }
            for (int i = 0; i < 9; i++)
            {
                if (buttons[i] != null)
                {
                    grid.Controls.Add(buttons[i], i % 3, i / 3);
                }
            }
            return grid;
        }

        private Button[] CreateCopyRoseGrid()
        {
            var buttons = new Button[9];
            string[] labels = { "", "↑", "", "←", "N", "→", "", "↓", "" };
            for (int i = 0; i < 9; i++)
            {
                if (i == 0 || i == 2 || i == 6 || i == 8)
                {
                    buttons[i] = null!;
                    continue;
                }

                int index = i;
                buttons[i] = new Button
                {
                    Text = labels[i],
                    Dock = DockStyle.Fill,
                    Margin = new Padding(1),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI Symbol", 9f)
                };
                buttons[i].Click += (s, e) =>
                {
                    string sideVal = "None";
                    if (index == 1) sideVal = "U";
                    else if (index == 3) sideVal = "L";
                    else if (index == 5) sideVal = "R";
                    else if (index == 7) sideVal = "D";

                    SetCopySide(sideVal);
                };
            }
            return buttons;
        }
    }

    public class ToolProfile
    {
        public string Name { get; set; } = "";
        public string SizeMode { get; set; } = "Stretch (Loft)";
        public string Alignment { get; set; } = "C";
        public string Depth { get; set; } = "Mid";
        public float Thickness { get; set; } = 0;
        public bool UsePercentageThick { get; set; } = false;
        public float OffsetA { get; set; } = 0;
        public bool UsePercentageOffsetA { get; set; } = false;
        public float OffsetB { get; set; } = 0;
        public bool UsePercentageOffsetB { get; set; } = false;
        public int ValidationMode { get; set; } = 0;
        public int RotationOffset { get; set; } = 0;
        public string CopySide { get; set; } = "None";
        public bool Triangulate { get; set; } = false;
        public string SplitMode { get; set; } = "One Solid (Convex)";
        public int Slices { get; set; } = 1;
    }

    public class BrushBuilderSettings
    {
        public int WindowX { get; set; } = int.MinValue;
        public int WindowY { get; set; } = int.MinValue;
        public string SizeMode { get; set; } = "Stretch (Loft)";
        public string Alignment { get; set; } = "C";
        public string Depth { get; set; } = "Mid";
        public int ValidationIndex { get; set; } = 0;
        public decimal Thickness { get; set; } = 0;
        public bool UsePercentageThick { get; set; } = false;
        public decimal OffsetA { get; set; } = 0;
        public bool UsePercentageOffsetA { get; set; } = false;
        public decimal OffsetB { get; set; } = 0;
        public bool UsePercentageOffsetB { get; set; } = false;
        public bool ShowHoverHelper { get; set; } = true;
        public string CopySide { get; set; } = "None";
        public bool Triangulate { get; set; } = false;
        public string SplitMode { get; set; } = "One Solid (Convex)";
        public int Slices { get; set; } = 1;
    }
}
