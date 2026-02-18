using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DentalID.Desktop.ViewModels;

public partial class ToothViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _fdiNumber;

    [ObservableProperty]
    private bool _isPresent = true;

    public bool IsMissing => !IsPresent;

    [ObservableProperty]
    private bool _hasPathology;

    [ObservableProperty]
    private IBrush _fillColor;

    [ObservableProperty]
    private string _tooltipText;

    public List<string> Pathologies { get; set; } = new();

    private Avalonia.Media.Geometry? _geometry;

    public ToothViewModel(int fdiNumber)
    {
        FdiNumber = fdiNumber;
        FillColor = Brushes.White;
        TooltipText = $"Tooth #{fdiNumber}";
        
        _pathData = DentalID.Desktop.Assets.ToothShapes.GetPathForFdi(fdiNumber);
    }

    private string _pathData;

    public Avalonia.Media.Geometry? Geometry
    {
        get
        {
            if (_geometry == null)
            {
                try 
                { 
                    _geometry = Avalonia.Media.Geometry.Parse(_pathData); 
                }
                catch (System.Exception ex)
                { 
                    // Ignore in tests/headless but log for debug
                    System.Diagnostics.Debug.WriteLine($"Error parsing geometry: {ex.Message}");
                    _geometry = null;
                }
            }
            return _geometry;
        }
    }

    public void Reset()
    {
        IsPresent = true;
        HasPathology = false;
        Pathologies.Clear();
        FillColor = Brushes.White; // Default healthy color
        TooltipText = $"Tooth #{FdiNumber}";
    }

    public void MarkPathology(string pathology)
    {
        HasPathology = true;
        Pathologies.Add(pathology);
        
        // Forensic Color Coding
        FillColor = pathology switch
        {
            "Caries" => Brushes.Red,
            "Crown" => Brushes.Gold,
            "Filling" => Brushes.Blue,
            "Implant" => Brushes.SlateGray,
            "Periapical lesion" => Brushes.Orange,
            "Root Piece" => Brushes.Brown,
            "Root canal obturation" => Brushes.DarkGreen,
            "Missing teeth" => Brushes.Transparent, // Or handle differently logic-wise
            "Deep Caries" => Brushes.DarkRed, // Legacy support just in case
            _ => Brushes.Red // Default
        };

        TooltipText = $"#{FdiNumber}: {string.Join(", ", Pathologies)}";
    }
    
    public void MarkHealthy()
    {
         if (!HasPathology)
         {
             FillColor = Brushes.White;
         }
    }
    
    public void MarkTreatment(TreatmentItem treatment)
    {
        // In a real app, treatments might add layers or icons.
        // For now, we change the fill color.
        try
        {
            FillColor = Brush.Parse(treatment.Color);
        }
        catch
        {
            FillColor = Brushes.Gray;
        }
        
        TooltipText = $"#{FdiNumber}: {treatment.Name} ({treatment.Category})";
    }
}
