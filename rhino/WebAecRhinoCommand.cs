using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;

namespace WebAecRhino
{
    [Guid("b01e6928-19b2-4524-9679-ac583a83fdf9")]
    public class WebAecRhinoPanelHost : RhinoWindows.Controls.WpfElementHost
    {
        public WebAecRhinoPanelHost()
            : base(new WebAecRhinoView(), null)
        {
        }
    }

    public class WebAecRhinoCommand : Command
    {
        public WebAecRhinoCommand()
        {
            Instance = this;

            Rhino.UI.Panels.RegisterPanel(
                WebAecRhinoPlugin.Instance,
                typeof(WebAecRhinoPanelHost),
                "Web AEC Rhino",
                System.Drawing.SystemIcons.Application
            );
        }

        ///<summary>The only instance of this command.</summary>
        public static WebAecRhinoCommand? Instance { get; private set; }


        ///<returns>The command name as it appears on the Rhino command line.</returns>
        public override string EnglishName => "WebAecRhinoCommand";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var panelId = typeof(WebAecRhinoPanelHost).GUID;
            Rhino.UI.Panels.OpenPanel(panelId);
            return Result.Success;
        }
    }
}
