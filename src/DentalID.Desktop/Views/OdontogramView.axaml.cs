using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DentalID.Desktop.Views;

public partial class OdontogramView : UserControl
{
    public OdontogramView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    // Pragma disable for obsolete DragDrop API (DoDragDrop -> DoDragDropAsync transition is complex without docs)
#pragma warning disable CS0618 
    private async void OnTreatmentPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        try
        {
            if (sender is Control control && control.DataContext is DentalID.Desktop.ViewModels.TreatmentItem treatment)
            {
                var data = new Avalonia.Input.DataObject();
                data.Set("Treatment", treatment.Name);

                // Start Drag
                await Avalonia.Input.DragDrop.DoDragDrop(e, data, Avalonia.Input.DragDropEffects.Copy);
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drag drop failed: {ex}");
        }
    }

    private void OnToothDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (e.Data.Contains("Treatment"))
            e.DragEffects = Avalonia.Input.DragDropEffects.Copy;
        else
            e.DragEffects = Avalonia.Input.DragDropEffects.None;
    }

    private void OnToothDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        if (sender is Control control && 
            control.DataContext is DentalID.Desktop.ViewModels.ToothViewModel tooth &&
            DataContext is DentalID.Desktop.ViewModels.OdontogramViewModel vm)
        {
            if (e.Data.Contains("Treatment"))
            {
                var treatmentName = e.Data.Get("Treatment") as string;
                if (!string.IsNullOrEmpty(treatmentName))
                {
                    vm.OnTreatmentDropped(tooth.FdiNumber, treatmentName);
                }
            }
        }
    }
#pragma warning restore CS0618
}
