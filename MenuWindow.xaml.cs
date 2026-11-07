using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Calisto;

/// <summary>One row in the results list; expands to show the meaning when clicked.</summary>
public sealed class ResultItem : INotifyPropertyChanged
{
    public AbbreviationHit Hit { get; }
    private string _meaning = "";
    private string _source = "";
    private bool _expanded;

    public ResultItem(AbbreviationHit hit) => Hit = hit;

    public string Abbreviation => Hit.Abbreviation;
    public string Meaning { get => _meaning; set { _meaning = value; Changed(nameof(Meaning)); } }
    public string Source { get => _source; set { _source = value; Changed(nameof(Source)); } }
    public bool Expanded { get => _expanded; set { _expanded = value; Changed(nameof(Expanded)); Changed(nameof(DetailVisibility)); Changed(nameof(Chevron)); } }
    public Visibility DetailVisibility => Expanded ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>Small triangle: points right when closed, down when open.</summary>
    public string Chevron => Expanded ? "▼" : "▶";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>The draggable, translucent menu that opens from the floating planet.</summary>
public partial class MenuWindow : Window
{
    private readonly FloatingIconWindow _owner;
    public event Func<Task>? ScanRequested;
    private bool _placedByUser;

    public MenuWindow(FloatingIconWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        OpacitySlider.Value = App.Settings.Opacity;
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW);
        Closing += (_, e) => { e.Cancel = true; Hide(); };   // never destroy; the icon reuses it
        ApplyOpacity();
    }

    public void ApplyOpacity()
    {
        Opacity = App.Settings.Opacity;
        OpacityLabel.Text = $"{Math.Round(App.Settings.Opacity * 100)}%";
    }

    /// <summary>Opens the menu beside the icon (unless the user dragged it somewhere else this session).</summary>
    public void ShowNextTo(FloatingIconWindow icon)
    {
        Message.Visibility = Visibility.Collapsed;
        if (!_placedByUser)
        {
            // Measure first so the panel can be kept on screen.
            Show();
            UpdateLayout();
            var area = SystemParameters.WorkArea;
            double left = icon.Left - ActualWidth - 8;
            if (left < area.Left + 4) left = icon.Left + icon.Width + 8;
            double top = icon.Top + icon.Height / 2 - ActualHeight / 2;
            Left = Math.Clamp(left, area.Left + 4, Math.Max(area.Left + 4, area.Right - ActualWidth - 4));
            Top = Math.Clamp(top, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - ActualHeight - 4));
        }
        else Show();
        Activate();
    }

    public void ShowResults(List<AbbreviationHit> hits, string windowTitle)
    {
        MainMenu.Visibility = Visibility.Collapsed;
        Results.Visibility = Visibility.Visible;
        var title = string.IsNullOrWhiteSpace(windowTitle) ? "that window" : $"\"{Shorten(windowTitle, 40)}\"";
        if (hits.Count == 0)
        {
            ResultsTitle.Text = "No abbreviations found";
            ResultsSubtitle.Text = $"Nothing that looks like an abbreviation in {title}. Some apps do not expose their text to Windows.";
        }
        else
        {
            ResultsTitle.Text = $"{hits.Count} abbreviation{(hits.Count == 1 ? "" : "s")} found";
            ResultsSubtitle.Text = $"In {title}. Click one to see what it means.";
        }
        ResultList.ItemsSource = hits
            .GroupBy(h => h.Abbreviation)
            .Select(g => new ResultItem(g.First()))
            .ToList();
    }

    public void ShowMessage(string text)
    {
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
        if (!IsVisible) ShowNextTo(_owner);
    }

    private static string Shorten(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); _placedByUser = true; } catch { }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (ScanRequested is { } handler) await handler();
    }

    private async void Result_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ResultItem item) return;
        if (item.Expanded) { item.Expanded = false; return; }
        item.Expanded = true;
        if (!string.IsNullOrEmpty(item.Meaning)) return;
        item.Meaning = "Looking up...";
        var result = await AbbreviationLookup.LookupAsync(item.Hit.Abbreviation, item.Hit.Context);
        item.Meaning = result.Text;
        item.Source = result.Source;
    }

    public void SetSliderForTest(double o) => OpacitySlider.Value = o;

    public int ResultCount => (ResultList.ItemsSource as List<ResultItem>)?.Count ?? 0;

    public async Task ExpandFirstForTest()
    {
        if (ResultList.ItemsSource is not List<ResultItem> { Count: > 0 } items) return;
        var item = items[0];
        item.Expanded = true;
        var result = await AbbreviationLookup.LookupAsync(item.Hit.Abbreviation, item.Hit.Context);
        item.Meaning = result.Text;
        item.Source = result.Source;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Results.Visibility = Visibility.Collapsed;
        MainMenu.Visibility = Visibility.Visible;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        Message.Text = "Checking for updates...";
        Message.Visibility = Visibility.Visible;
        var result = await UpdateChecker.CheckAsync(App.Settings.UpdateUrl);
        if (result.Error is not null) { Message.Text = "Could not check for updates. " + result.Error; return; }
        if (!result.IsNewer) { Message.Text = $"You have the newest version ({AppInfo.Current.ToString(3)})."; return; }
        Message.Text = $"Version {result.LatestVersion} is available (you have {AppInfo.Current.ToString(3)}).";
        if (result.DownloadUrl is not null)
        {
            var open = MessageBox.Show(Message.Text + "\n\nOpen the download page?", AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.DownloadUrl) { UseShellExecute = true });
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void Exit_Click(object sender, RoutedEventArgs e) => _owner.ExitApp();
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        App.Settings.Opacity = Math.Round(e.NewValue, 2);
        App.Settings.Save();
        _owner.ApplyOpacity();
        ApplyOpacity();
    }
}
