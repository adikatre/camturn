using System.Windows;
using CamTurn.App.ViewModels;

namespace CamTurn.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly SettingsViewModel _vm;
    private readonly PreviewRenderer _preview;

    public SettingsWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        _vm = new SettingsViewModel(host);
        DataContext = _vm;
        _preview = new PreviewRenderer(Preview, Dispatcher, () => PreviewPlaceholder.Visibility = Visibility.Collapsed);
        Loaded += async (_, _) => await _vm.LoadAsync();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _host.PreviewFrame += _preview.OnFrame;
            }
            else
            {
                _host.PreviewFrame -= _preview.OnFrame;
            }

            _host.SetPreviewActive(IsVisible);
        };
        Closed += (_, _) =>
        {
            _host.PreviewFrame -= _preview.OnFrame;
            _host.SetPreviewActive(false);
            _vm.Dispose();
        };
    }

    private void OnManualChecked(object sender, RoutedEventArgs e) => _vm.AutoRotation = false;
}
