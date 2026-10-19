using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GalacticDiagnosis.Gui.Services;
using GalacticDiagnosis.Gui.ViewModels;

namespace GalacticDiagnosis.Gui.Views;

public partial class ReportView : UserControl
{
    // NavigateToString's documented limit is 2 MB; real reports are tens of KB.
    const int MaxInlineHtml = 1_500_000;

    ReportViewModel? _vm;

    public ReportView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as ReportViewModel);
        Loaded += async (_, _) => await ShowSelectedAsync();
        Unloaded += (_, _) => Attach(null);
    }

    void Attach(ReportViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
    }

    async void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportViewModel.Selected)) await ShowSelectedAsync();
    }

    async Task ShowSelectedAsync()
    {
        if (_vm?.Selected is not { } tab || !IsLoaded || !WebViewHost.Available) return;
        try
        {
            await WebViewHost.InitAsync(Web);
            if (tab.Html.Length <= MaxInlineHtml) Web.CoreWebView2.NavigateToString(tab.Html);
            else _vm.OpenInBrowserCommand.Execute(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException
                                       or Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            // The runtime is there but won't start (damaged install, policy): fall back to the browser.
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
                main.Error = $"The report viewer couldn't start ({ex.Message}). Opening the report in the web browser instead.";
            _vm.OpenInBrowserCommand.Execute(null);
        }
    }
}
