using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yukkuri;
using Yukkuri.Services;

internal static class UiChecks
{
    public static Task<IReadOnlyList<string>> RunAsync(string root, IReadOnlyList<ClassicVoicePack> voices)
    {
        var complete = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                var app = new App(startDesktop: false);
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var storage = new Storage(Path.Combine(root, "ui"));
                var selected = voices.FirstOrDefault(v => v.Id == "at2-aq_f1c") ?? voices[0];
                storage.SaveSettings(new UserSettings
                {
                    AutoGenerate = false, ListenToQQ = false, KeepHistory = false,
                    ClassicVoices = voices.ToList(), AquesDll = selected.Path,
                    AquesEngine = selected.Engine, AquesPhont = selected.PhontPath, AquesPreset = selected.Preset
                });
                window = new MainWindow(storage, enableQQConnection: false);
                var vm = (MainViewModel)window.DataContext;
                var content = (FrameworkElement)window.Content;
                var checkedItems = new List<string>();
                var output = Path.Combine(root, "screenshots");
                Directory.CreateDirectory(output);
                foreach (var size in new[] { new Size(1200, 820), new Size(1000, 700), new Size(984, 661) })
                {
                    foreach (var page in new[] { "ComposeNav", "SettingsNav" })
                    {
                        ((RadioButton)window.FindName(page)).IsChecked = true;
                        Layout(content, size);
                        Render(content, size, Path.Combine(output, $"{page}-{size.Width:0}x{size.Height:0}.png"));
                        var editor = (TextBox)window.FindName("DraftEditor");
                        if (page == "ComposeNav" && (editor.ActualWidth < 100 || editor.ActualHeight < 100))
                            throw new Exception("Composer editor collapsed at supported window size");
                        checkedItems.Add($"{page} renders at {size.Width}x{size.Height}");
                    }
                }
                ((RadioButton)window.FindName("ComposeNav")).IsChecked = true;
                Layout(content, new Size(1200, 820));
                var controls = Descendants(content).ToList();
                var voicePicker = controls.OfType<ComboBox>().Single(c =>
                    System.Windows.Automation.AutomationProperties.GetName(c) == "选择音色");
                if (voicePicker.GroupStyle.Count != 1 || voicePicker.Items.Count < 8)
                    throw new Exception("Grouped voice picker missing");
                checkedItems.Add("Grouped engine voice picker is populated");
                var sliders = controls.OfType<Slider>().Where(s => s.IsVisible || s.Name == "").ToList();
                if (sliders.Count != 1 ||
                    System.Windows.Automation.AutomationProperties.GetName(sliders[0]) != "语速")
                    throw new Exception("Unexpected timbre adjustment controls");
                checkedItems.Add("Speed is the only timbre adjustment slider");
                if (controls.OfType<ComboBox>().Any(c =>
                    System.Windows.Automation.AutomationProperties.GetName(c).Contains("录音")))
                    throw new Exception("Obsolete microphone controls remain");
                checkedItems.Add("Microphone routing controls removed");
                if (controls.OfType<PasswordBox>().Any() || controls.OfType<Button>().Any(b => Equals(b.Content, "获取令牌")))
                    throw new Exception("Obsolete online authentication controls remain");
                checkedItems.Add("Online registration and token UI removed");

                var mini = new MiniWindow(window);
                var miniContent = (FrameworkElement)mini.Content;
                Layout(miniContent, new Size(410, 300));
                var miniEditor = (TextBox)mini.FindName("FloatingEditor");
                if (miniEditor.IsReadOnly || !miniEditor.AcceptsReturn || miniEditor.MaxLength != 240 || miniEditor.ActualHeight < 80)
                    throw new Exception("Floating editor is not usable");
                vm.CurrentClip = new VoiceClip { FilePath = Path.Combine(storage.AudioDirectory, "old.wav") };
                miniEditor.Text = "悬浮窗直接编辑文字。\n第二行同步到工作台。";
                miniEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                if (vm.Text != miniEditor.Text || vm.HasClip) throw new Exception("Floating editor binding failed");
                checkedItems.Add("Floating editor updates shared text and invalidates old audio");
                vm.Text = "主窗口修改也会同步到悬浮窗。";
                miniEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
                if (miniEditor.Text != vm.Text) throw new Exception("Floating text reverse sync failed");
                checkedItems.Add("Shared text updates floating editor in both directions");
                if (Descendants(miniContent).OfType<ComboBox>().Any()) throw new Exception("Obsolete recipient picker remains");
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Layout(miniContent, new Size(410, 300));
                Render(miniContent, new Size(410, 300), Path.Combine(output, "floating-window.png"));
                mini.Close();
                checkedItems.Add("Floating window renders with multiline editor and active-chat label");
                window.Close();
                window = null;
                app.Shutdown();
                complete.SetResult(checkedItems);
            }
            catch (Exception error) { window?.Close(); complete.TrySetException(error); }
        }) { IsBackground = true, Name = "Offscreen WPF UI checks" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return complete.Task;
    }

    private static void Layout(FrameworkElement content, Size size)
    {
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
    }

    private static void Render(FrameworkElement content, Size size, string destination)
    {
        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(Brushes.White, null, new Rect(size));
        image.Render(background);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(destination);
        encoder.Save(output);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
