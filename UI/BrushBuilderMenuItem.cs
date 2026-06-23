using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Threading.Tasks;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using LogicAndTrick.Oy;

namespace HammerTime.BrushBuilder.UI
{
    [Export(typeof(IMenuItem))]
    public class BrushBuilderMenuItem : IMenuItem
    {
        [Import]
        private Tools.BrushBuilderTool _tool = null!;

        public string ID => "HammerTime_BrushBuilder_Activate";
        public string Name => "Brush Builder Tool";
        public string Description => "Create bridge/fill brushes between two selected faces";
        public Image? Icon => null;
        public bool AllowedInToolbar => true;

        public string Section => "Tools";
        public string Path => "";
        public string Group => "Tools";
        public string OrderHint => "Y";
        public string ShortcutText => "Shift+B";

        public bool IsToggle => false;
        public bool GetToggleState(IContext context) => false;

        public bool IsInContext(IContext context) => true;

        public async Task Invoke(IContext context)
        {
            await Oy.Publish("Tool:Activated", _tool);
        }
    }
}
