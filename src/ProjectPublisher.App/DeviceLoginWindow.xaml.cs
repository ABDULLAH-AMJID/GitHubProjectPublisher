using System.Diagnostics;
using System.Windows;
using ProjectPublisher.Models;

namespace ProjectPublisher;

public partial class DeviceLoginWindow : Window
{
    private readonly DeviceCodeResponse _deviceCode;
    private bool _completed;

    public CancellationTokenSource Cancellation { get; } = new();

    public DeviceLoginWindow(DeviceCodeResponse deviceCode)
    {
        InitializeComponent();
        _deviceCode = deviceCode;
        CodeText.Text = deviceCode.UserCode;
        Loaded += (_, _) =>
        {
            TryCopyCode();
            OpenBrowser();
        };
        Closed += (_, _) =>
        {
            if (!_completed) Cancellation.Cancel();
        };
    }

    public void MarkCompleted() => _completed = true;

    private void Copy_Click(object sender, RoutedEventArgs e) => TryCopyCode();

    private void Open_Click(object sender, RoutedEventArgs e) => OpenBrowser();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Cancellation.Cancel();
        Close();
    }

    private void TryCopyCode()
    {
        try
        {
            Clipboard.SetText(_deviceCode.UserCode);
            CopyButton.Content = "Copied";
        }
        catch
        {
            CopyButton.Content = "Copy";
        }
    }

    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_deviceCode.VerificationUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
