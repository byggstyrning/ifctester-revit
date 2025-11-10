using System.Windows.Controls;

namespace WebAecRhino;

public class WebAecRhinoView : UserControl
{
    public WebAecRhinoView()
    {
        var grid = new System.Windows.Controls.Grid();
        var textBlock = new TextBlock
        {
            Text = "Web Aec Rhino",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        grid.Background = System.Windows.Media.Brushes.White;

        Content = grid;
        grid.Children.Add(textBlock);
    }
}