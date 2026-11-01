using System;
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
        Loaded += async (_, _) =>
        {
            _host.PreviewFrame += _preview.OnFrame;
            _host.SetPreviewActive(true);
            await _vm.LoadAsync();
        };
        Closed += (_, _) =>
        {
            _host.PreviewFrame -= _preview.OnFrame;
            _host.SetPreviewActive(false);
        };
    }

    private void OnManualChecked(object sender, RoutedEventArgs e) => _vm.AutoRotation = false;
}
