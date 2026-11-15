namespace Yukkuri;

public partial class MiniWindow : Window
{
    private readonly MainWindow _main;
    public MiniWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        DataContext = main.DataContext;
        Left = Math.Max(0, SystemParameters.WorkArea.Right - Width - 24);
        Top = Math.Max(0, SystemParameters.WorkArea.Bottom - Height - 30);
    }
    private async void Preview_Click(object sender, RoutedEventArgs e) => await _main.PreviewAsync();
    private async void Editor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter ||
            System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        e.Handled = true;
        await _main.GenerateAsync();
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => _main.StopPlayback();
    private async void Send_Click(object sender, RoutedEventArgs e) => await _main.SendToQQAsync();
    private void Main_Click(object sender, RoutedEventArgs e) => _main.Restore();
}
