using System.Windows;

namespace Calisto;

public enum SetupChoice { Cancel, Install, RunPortable }

public partial class SetupWindow : Window
{
    public SetupChoice Choice { get; private set; } = SetupChoice.Cancel;

    public SetupWindow(bool installedCopy = false)
    {
        InitializeComponent();
        if (installedCopy)
        {
            // Already installed: this is only the permission screen.
            InstallNote.Visibility = Visibility.Collapsed;
            PortableBtn.Visibility = Visibility.Collapsed;
            InstallBtn.Content = "I agree, start Calisto";
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e) { Choice = SetupChoice.Install; DialogResult = true; Close(); }
    private void Portable_Click(object sender, RoutedEventArgs e) { Choice = SetupChoice.RunPortable; DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Choice = SetupChoice.Cancel; DialogResult = false; Close(); }
}
