using System.Windows.Controls;

namespace WebAecRevit.Views;

public sealed class WebAecRevitView : UserControl
{
    public WebAecRevitView()
    {
        var grid = new System.Windows.Controls.Grid();
        var textBlock = new TextBlock
        {
            Text = "WebAecRevit",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        grid.Background = System.Windows.Media.Brushes.White;

        Content = grid;
        grid.Children.Add(textBlock);
    }
}