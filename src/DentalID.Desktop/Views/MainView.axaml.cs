using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DentalID.Desktop.ViewModels;

namespace DentalID.Desktop.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
