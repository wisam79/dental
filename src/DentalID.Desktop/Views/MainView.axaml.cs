using System.Diagnostics;
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DentalID.Desktop.ViewModels;
using DentalID.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DentalID.Desktop.Views;

public partial class MainView : UserControl
{
    private MainWindowViewModel? _boundVm;
    private ContentControl? _mainContentHost;
    private ContentControl? _bootContentHost;

    public MainView()
    {
        InitializeComponent();

        _mainContentHost = this.FindControl<ContentControl>("MainContentHost");
        _bootContentHost = this.FindControl<ContentControl>("BootContentHost");

        DataContextChanged += OnDataContextChanged;

        // Add SelectionChanged event handler for debugging
        var navListBox = this.FindControl<ListBox>("NavListBox");
        if (navListBox != null)
        {
            navListBox.SelectionChanged += (s, e) =>
            {
                Debug.WriteLine($"[DEBUG] MainView: NavListBox SelectionChanged to index {navListBox.SelectedIndex}");
            };
        }
        else
        {
            Debug.WriteLine("[ERROR] MainView: NavListBox not found!");
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundVm != null)
        {
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
            _boundVm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _boundVm = vm;
            _boundVm.PropertyChanged += OnVmPropertyChanged;
            PushContent(vm.CurrentContent);
            LogInfo($"[VIEW] MainView bound to VM. CurrentContent={vm.CurrentContent?.GetType().Name ?? "null"}");
        }
        else
        {
            PushContent(null);
            LogInfo("[VIEW] MainView DataContext is not MainWindowViewModel.");
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundVm == null)
            return;

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentContent) ||
            e.PropertyName == nameof(MainWindowViewModel.IsShellVisible))
        {
            PushContent(_boundVm.CurrentContent);
            LogInfo($"[VIEW] MainView content updated => {_boundVm.CurrentContent?.GetType().Name ?? "null"}, IsShellVisible={_boundVm.IsShellVisible}");
        }
    }

    private void PushContent(object? content)
    {
        if (_boundVm?.IsShellVisible == true)
        {
            if (_bootContentHost != null)
                _bootContentHost.Content = null;
            if (_mainContentHost != null)
                _mainContentHost.Content = content;
        }
        else
        {
            if (_mainContentHost != null)
                _mainContentHost.Content = null;
            if (_bootContentHost != null)
                _bootContentHost.Content = content;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Button)
        {
            return;
        }

        if (VisualRoot is Window window)
        {
            if (e.ClickCount == 2)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            window.BeginMoveDrag(e);
        }
    }

    private static void LogInfo(string message)
    {
        try
        {
            var logger = App.Services?.GetService<ILoggerService>();
            logger?.LogInformation(message);
        }
        catch
        {
            // Swallow logging failures in view layer diagnostics.
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
