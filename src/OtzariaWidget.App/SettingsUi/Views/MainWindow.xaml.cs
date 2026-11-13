using System.ComponentModel;
using System.Windows;
using OtzariaWidget.App.SettingsUi.ViewModels;

namespace OtzariaWidget.App.SettingsUi.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Session.SaveFailed)) UpdateSaveIndicator(vm);
        };
        UpdateSaveIndicator(vm);
    }

    private void UpdateSaveIndicator(MainViewModel vm)
    {
        SaveGlyph.Text = vm.Session.SaveFailed ? "" : "";
        SaveGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            vm.Session.SaveFailed ? "StateErrorBrush" : "StateOkBrush");
    }

    /// <summary>
    /// Diagnostics: renders the window content to a PNG (used for visual QA: "--screenshot file.png [--page N]"),
    /// then closes the window. Independent of DWM/foreground state.
    /// </summary>
    public void ScheduleScreenshot(string path, TimeSpan delay)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var root = (System.Windows.FrameworkElement)Content;
                double dpi = 1.0;
                int w = (int)Math.Ceiling(root.ActualWidth * dpi), h = (int)Math.Ceiling(root.ActualHeight * dpi);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var bg = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = (TryFindResource("SolidBackgroundFillColorBaseBrush") as System.Windows.Media.Brush) ?? Background };
                bg.Measure(new Size(w, h)); bg.Arrange(new Rect(0, 0, w, h));
                bmp.Render(bg);
                // The window's RTL mirroring lives above the content root, so mirror it back when rendering the root alone.
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.PushTransform(new System.Windows.Media.ScaleTransform(-1, 1, w / 2.0, 0));
                    dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, w, h));
                    dc.Pop();
                }
                bmp.Render(dv);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = System.IO.File.Create(path);
                enc.Save(fs);
            }
            catch (Exception ex) { Infrastructure.SettingsLog.Error("Screenshot failed", ex); }
            Close();
        };
        timer.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try { (DataContext as MainViewModel)?.Session.Flush(); } catch { }
        base.OnClosing(e);
    }

    /// <summary>Called when a second launch asks the running instance to come forward.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
