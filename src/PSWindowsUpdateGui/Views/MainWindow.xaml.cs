using System;
using System.Collections.Generic;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PSWindowsUpdateGui.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppThemeService _themeService;
    private readonly IUserDialogService _dialogs;
    private int _windowWidth = 1320;
    private int _windowHeight = 880;

    internal MainViewModel ViewModel => _viewModel;
    internal FrameworkElement RootElement => RootGrid;

    internal MainWindow(MainViewModel viewModel, AppThemeService themeService, IUserDialogService dialogs)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _themeService = themeService;
        _dialogs = dialogs;
        RootGrid.DataContext = viewModel;
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        SizeAndCenterWindow();
        ApplyTheme();
        _themeService.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
        RootGrid.Loaded += OnRootLoaded;
        MainNavigation.SelectedItem = MainNavigation.MenuItems[0];
        ShowPage(0);
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootLoaded;
        await _viewModel.InitializeAsync();
    }

    private void SizeAndCenterWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (area == null) return;
        var work = area.WorkArea;
        _windowWidth = Math.Min(1440, Math.Max(1, work.Width - 40));
        _windowHeight = Math.Min(960, Math.Max(1, work.Height - 40));
        AppWindow.Resize(new SizeInt32(_windowWidth, _windowHeight));
        AppWindow.Move(new PointInt32(work.X + Math.Max(0, (work.Width - _windowWidth) / 2), work.Y + Math.Max(0, (work.Height - _windowHeight) / 2)));
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _themeService.ElementTheme;
        AppWindow.TitleBar.PreferredTheme = _themeService.TitleBarTheme;
    }

    private void MainNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;
        var index = tag switch { "history" => 1, "services" => 2, "offline" => 3, "operations" => 4, "logs" => 5, _ => 0 };
        ShowPage(index);
    }

    internal void ShowPage(int index)
    {
        UpdatesPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ServicesPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        OfflinePage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        OperationsPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.SelectedMainTabIndex = index;
    }

    private async void BrowseOfflineCab_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".cab");
        picker.FileTypeFilter.Add(".*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file != null) _viewModel.OfflineCabPath = file.Path;
    }

    private async void CliHelp_Click(object sender, RoutedEventArgs e) => await _dialogs.ShowMessageAsync(
        "CLI help",
        "Run PSWindowsUpdateGUI.exe help from an elevated PowerShell or Command Prompt. Use --output json for automation and --plan before modifying commands.");

    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"PSWindowsUpdateGUI-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add("Text log", new List<string> { ".log" });
        picker.FileTypeChoices.Add("Text file", new List<string> { ".txt" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file != null) await FileIO.WriteTextAsync(file, _viewModel.LogText);
    }

    private async void About_Click(object sender, RoutedEventArgs e) => await _dialogs.ShowMessageAsync(
        "About PSWindowsUpdate GUI",
        "PSWindowsUpdate GUI 3.0.0-beta.1\n\nPortable Windows 11 x64 GUI and CLI built directly on Windows Update Agent with WinUI 3 and the Windows App SDK.\n\nSingle-file, self-contained distribution. MIT License.");

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        _viewModel.Dispose();
        Application.Current.Exit();
    }
}
