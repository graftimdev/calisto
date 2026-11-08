using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Calisto;

/// <summary>The draggable, translucent menu that opens from the floating planet. Pages: main, settings, API key, contribute.</summary>
public partial class MenuWindow : Window
{
    private readonly FloatingIconWindow _owner;
    private bool _placedByUser;
    private bool _loading;
    private CancellationTokenSource? _packCts;

    public MenuWindow(FloatingIconWindow owner)
    {
        InitializeComponent();
        _owner = owner;
        _loading = true;
        OpacitySlider.Value = App.Settings.Opacity;
        PassiveToggle.IsChecked = App.Settings.PassiveScan;
        LanguageBox.ItemsSource = LanguageDetector.Choices.Select(c => new { c.Code, c.Name }).ToList();
        LanguageBox.SelectedValue = LanguageDetector.Choices.Any(c => c.Code == App.Settings.Language) ? App.Settings.Language : "auto";
        _loading = false;
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW);
        Closing += (_, e) => { e.Cancel = true; Hide(); };   // never destroy; the icon reuses it
        ApplyOpacity();
        RefreshState();
    }

    public void ApplyOpacity()
    {
        Opacity = App.Settings.Opacity;
        OpacityLabel.Text = $"{Math.Round(App.Settings.Opacity * 100)}%";
    }

    private void RefreshState()
    {
        ApiState.Text = string.IsNullOrWhiteSpace(App.Settings.ApiKey) ? "" : "(saved)";
        RemoveApiBtn.Visibility = string.IsNullOrWhiteSpace(App.Settings.ApiKey) ? Visibility.Collapsed : Visibility.Visible;
        HotkeyHint.Text = $"Select a word anywhere and press {App.Settings.Hotkey} to see its meaning.";
        HotkeyBox.Text = App.Settings.Hotkey;
        var lang = App.Settings.Language == "auto" ? "en" : App.Settings.Language;
        PackState.Text = OfflineStore.HasPack(lang)
            ? $"{LanguageDetector.DisplayName(lang)}: {OfflineStore.PackEntryCount(lang):N0} entries saved for offline use."
            : $"Works offline with words you already looked up ({OfflineStore.CachedCount}). Download the English pack for more.";
        PackBtn.Content = OfflineStore.HasPack(lang) ? "Remove" : "Download";
    }

    /// <summary>Opens the menu beside the icon (unless the user dragged it somewhere else this session).</summary>
    public void ShowNextTo(FloatingIconWindow icon)
    {
        Message.Visibility = Visibility.Collapsed;
        RefreshState();
        if (!_placedByUser)
        {
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

    public void ShowMessage(string text)
    {
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
        if (!IsVisible) ShowNextTo(_owner);
    }

    private void ShowPage(StackPanel page, string title)
    {
        foreach (var p in new[] { MainPage, SettingsPage, ApiPage, ContributePage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        BackBtn.Visibility = page == MainPage ? Visibility.Collapsed : Visibility.Visible;
        Title.Text = title;
        Message.Visibility = Visibility.Collapsed;
        RefreshState();
        UpdateLayout();
        KeepOnScreen();
    }

    private void KeepOnScreen()
    {
        var area = SystemParameters.WorkArea;
        if (Top + ActualHeight > area.Bottom - 4) Top = Math.Max(area.Top + 4, area.Bottom - ActualHeight - 4);
    }

    public void ShowSettingsForTest() => ShowPage(SettingsPage, "Settings");

    // ---- navigation ----
    private void Back_Click(object sender, RoutedEventArgs e) => ShowPage(MainPage, "Calisto");
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, "Settings");
    private void AddApi_Click(object sender, RoutedEventArgs e) { ApiKeyBox.Password = App.Settings.ApiKey; ApiError.Visibility = Visibility.Collapsed; ShowPage(ApiPage, "Claude API key"); ApiKeyBox.Focus(); }
    private void Contribute_Click(object sender, RoutedEventArgs e) { ContribError.Visibility = Visibility.Collapsed; ContribOk.Visibility = Visibility.Collapsed; ShowPage(ContributePage, "Contribute"); ContribWord.Focus(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && !HotkeyBox.IsKeyboardFocused) Hide(); }
    private void Exit_Click(object sender, RoutedEventArgs e) => _owner.ExitApp();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); _placedByUser = true; } catch { }
    }

    // ---- API key ----
    private void SaveApi_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (key.Length > 0 && !key.StartsWith("sk-ant-", StringComparison.Ordinal))
        {
            ApiError.Text = "That does not look like a Claude API key (they start with sk-ant-).";
            ApiError.Visibility = Visibility.Visible;
            return;
        }
        App.Settings.ApiKey = key;
        App.Settings.Save();
        ShowPage(MainPage, "Calisto");
        ShowMessage(key.Length > 0 ? "API key saved. Lookups now use Claude as well." : "API key removed.");
    }

    private void RemoveApi_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ApiKey = "";
        App.Settings.Save();
        ShowPage(MainPage, "Calisto");
        ShowMessage("API key removed.");
    }

    // ---- settings ----
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || !IsLoaded) return;
        App.Settings.Opacity = Math.Round(e.NewValue, 2);
        App.Settings.Save();
        _owner.ApplyOpacity();
        ApplyOpacity();
    }

    private void PassiveToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.PassiveScan = PassiveToggle.IsChecked == true;
        App.Settings.Save();
        _owner.ApplyPassiveScan();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedValue is not string code) return;
        App.Settings.Language = code;
        App.Settings.Save();
        RefreshState();
    }

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) { HotkeyBox.Text = "Press the keys..."; HotkeyError.Visibility = Visibility.Collapsed; }
    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) { HotkeyBox.Text = App.Settings.Hotkey; }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        var mods = Keyboard.Modifiers;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            HotkeyBox.Text = new Hotkey(mods, Key.None).ToString().Replace("+None", "+...");
            return;
        }
        var problem = Hotkey.Problem(mods, key);
        var candidate = new Hotkey(mods, key);
        if (problem is null && !_owner.TryApplyHotkey(candidate))
            problem = $"{candidate} can't be used: Windows or another app already owns it.";
        if (problem is not null)
        {
            HotkeyError.Text = problem;
            HotkeyError.Visibility = Visibility.Visible;
            HotkeyBox.Text = candidate.ToString();
            return;
        }
        HotkeyError.Visibility = Visibility.Collapsed;
        App.Settings.Hotkey = candidate.ToString();
        App.Settings.Save();
        HotkeyBox.Text = candidate.ToString();
        Keyboard.ClearFocus();
        RefreshState();
    }

    private async void Pack_Click(object sender, RoutedEventArgs e)
    {
        var lang = App.Settings.Language == "auto" ? "en" : App.Settings.Language;
        if (OfflineStore.HasPack(lang))
        {
            OfflineStore.DeletePack(lang);
            RefreshState();
            return;
        }
        if (_packCts is not null) { _packCts.Cancel(); return; }
        _packCts = new CancellationTokenSource();
        PackBtn.Content = "Cancel";
        PackProgress.Visibility = Visibility.Visible;
        PackProgress.Value = 0;
        var progress = new Progress<double>(p => PackProgress.Value = p * 100);
        var error = await OfflineStore.DownloadPackAsync(lang, progress, _packCts.Token);
        _packCts = null;
        PackProgress.Visibility = Visibility.Collapsed;
        RefreshState();
        if (error is not null) ShowMessage(error);
    }

    // ---- contribute ----
    private void SubmitContribution_Click(object sender, RoutedEventArgs e)
    {
        var problem = Contribution.Validate(ContribWord.Text, ContribDef.Text);
        if (problem is not null)
        {
            ContribError.Text = problem;
            ContribError.Visibility = Visibility.Visible;
            ContribOk.Visibility = Visibility.Collapsed;
            return;
        }
        ContribError.Visibility = Visibility.Collapsed;
        var lang = App.Settings.Language != "auto" ? App.Settings.Language : LanguageDetector.Detect(ContribWord.Text + " " + ContribDef.Text);
        var url = Contribution.IssueUrl(ContribWord.Text, ContribDef.Text, lang);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            ContribOk.Text = "A GitHub page opened with your entry filled in. Press \"Submit new issue\" there and it joins the shared dictionary.";
            ContribOk.Visibility = Visibility.Visible;
            ContribWord.Text = ""; ContribDef.Text = "";
        }
        catch (Exception ex) { ContribError.Text = "Could not open the browser: " + ex.Message; ContribError.Visibility = Visibility.Visible; }
    }

    // ---- updates ----
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
}
