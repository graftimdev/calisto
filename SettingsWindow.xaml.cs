using System.Windows;

namespace Calisto;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        ApiKeyBox.Password = App.Settings.ApiKey;
        UpdateUrlBox.Text = App.Settings.UpdateUrl;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ApiKey = ApiKeyBox.Password.Trim();
        App.Settings.UpdateUrl = UpdateUrlBox.Text.Trim();
        App.Settings.Save();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
