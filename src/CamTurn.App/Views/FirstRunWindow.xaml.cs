using System;
using System.Windows;
using CamTurn.App.ViewModels;

namespace CamTurn.App.Views;

/// <summary>Five-step first-run wizard: Windows check, virtual camera, camera choice, rotation test, how to use it.</summary>
public partial class FirstRunWindow : Window
{
    private static readonly string[] Titles =
    [
        "Welcome to CamTurn",
        "Virtual camera",
        "Choose your camera",
        "Rotation test",
        "You're set",
    ];

    private readonly AppHost _host;
    private readonly SettingsViewModel _vm;
    private readonly PreviewRenderer _preview;
    private readonly UIElement[] _steps;
    private int _step;
    private bool _previewOn;

    public FirstRunWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        _vm = new SettingsViewModel(host);
        DataContext = _vm;
        _steps = [Step1, Step2, Step3, Step4, Step5];
        _preview = new PreviewRenderer(Preview, Dispatcher, () => PreviewPlaceholder.Visibility = Visibility.Collapsed);

        var build = Environment.OSVersion.Version.Build;
        WindowsCheck.Text = build >= 22000
            ? $"✓ Windows 11 (build {build}) supports CamTurn."
            : $"✗ This is Windows build {build}. CamTurn needs Windows 11 (build 22000 or newer) for its virtual camera.";

        Loaded += async (_, _) => await _vm.LoadAsync();
        Closed += (_, _) => SetPreview(false);
        Show(0);
    }

    private void Show(int step)
    {
        _step = step;
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        }

        StepTitle.Text = Titles[step];
        StepCounter.Text = $"Step {step + 1} of {_steps.Length}";
        BackButton.IsEnabled = step > 0;
        NextButton.Content = step == _steps.Length - 1 ? "_Finish" : "_Next";
        SetPreview(step == 3);
    }

    private void SetPreview(bool on)
    {
        if (on == _previewOn)
        {
            return;
        }

        _previewOn = on;
        if (on)
        {
            _host.PreviewFrame += _preview.OnFrame;
        }
        else
        {
            _host.PreviewFrame -= _preview.OnFrame;
        }

        _host.SetPreviewActive(on);
    }

    private void OnBack(object sender, RoutedEventArgs e) => Show(Math.Max(0, _step - 1));

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step < _steps.Length - 1)
        {
            if (_step == 1)
            {
                await _vm.RefreshVirtualCameraAsync();
            }

            Show(_step + 1);
            return;
        }

        await _host.UpdateConfigAsync(_host.Config with { FirstRunCompleted = true });
        Close();
    }
}
