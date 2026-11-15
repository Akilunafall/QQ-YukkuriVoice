using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace QQDraftFixture;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var window = new Window
        {
            Title = "QQ 输入适配验证（本地模拟，无发送功能）", Width = 620, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });
        var search = new TextBox { Height = 32, VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索");
        grid.Children.Add(search);
        var note = new TextBlock { Text = "本地 QQ NT 输入接口模拟：下方输入框公开可写 Group ValuePattern。", TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(note, 1); grid.Children.Add(note);
        var draft = new DraftBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10), FontSize = 16 };
        Grid.SetRow(draft, 2); grid.Children.Add(draft);
        window.Content = grid;
        new Application().Run(window);
    }
}

internal sealed class DraftBox : TextBox
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DraftPeer(this);
}

internal sealed class DraftPeer(DraftBox owner) : TextBoxAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    protected override string GetNameCore() => "QQ 草稿模拟";
}
