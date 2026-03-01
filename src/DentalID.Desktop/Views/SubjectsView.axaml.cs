using Avalonia.Controls;
using Avalonia;
using DentalID.Desktop.ViewModels;

namespace DentalID.Desktop.Views;

public partial class SubjectsView : UserControl
{
    private bool _initialized;

    public SubjectsView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        TryInitialize();
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        TryInitialize();
    }

    private void TryInitialize()
    {
        if (_initialized)
            return;

        if (DataContext is SubjectsViewModel vm)
        {
            _initialized = true;
            if (vm.InitializeCommand.CanExecute(null))
            {
                vm.InitializeCommand.Execute(null);
            }
        }
    }
}
