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
    private IBrush _strokeColor;

    [ObservableProperty]
    private double _strokeThickness = 1.0;

    [ObservableProperty]
    private string _tooltipText;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    private string _statusIcon = "";

    [ObservableProperty]
    private IBrush _statusIconColor;

    [ObservableProperty]
    private double _confidence;

    public List<string> Pathologies { get; set; } = new();

    private static readonly Dictionary<int, Avalonia.Media.Geometry> _geometryCache = new();
    private Avalonia.Media.Geometry? _geometry;

    public ToothViewModel(int fdiNumber)
    {
        FdiNumber = fdiNumber;
        FillColor = new SolidColorBrush(Color.Parse("#E8F0FE"));
        StrokeColor = new SolidColorBrush(Color.Parse("#90A4AE"));
        StatusIconColor = Brushes.Gray;
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
                if (_geometryCache.TryGetValue(FdiNumber, out var cached))
                {
                    _geometry = cached;
                }
                else
                {
                    try 
                    { 
                        _geometry = Avalonia.Media.Geometry.Parse(_pathData); 
                        _geometryCache[FdiNumber] = _geometry;
                    }
                    catch (System.Exception ex)
                    { 
                        throw new System.FormatException($"Failed to parse geometry for tooth {FdiNumber}: {ex.Message}", ex);
                    }
                }
            }
            return _geometry;
        }
    }

    public void Reset()
    {
        IsPresent = true;
        HasPathology = false;
        IsSelected = false;
        Confidence = 0;
        Pathologies.Clear();
        FillColor = new SolidColorBrush(Color.Parse("#E8F0FE"));
        StrokeColor = new SolidColorBrush(Color.Parse("#90A4AE"));
        StrokeThickness = 1.0;
        StatusIcon = "";
        Opacity = 1.0;
        TooltipText = $"Tooth #{FdiNumber}";
    }

    public void MarkPathology(string pathology)
    {
        HasPathology = true;
        Pathologies.Add(pathology);
        
        // Forensic Color Coding — premium, more subtle colors
        var (fill, stroke, icon) = pathology switch
        {
            "Caries" => ("#FFEBEE", "#EF5350", "⚠"),               // Soft red
            "Crown" => ("#FFF8E1", "#FFC107", "♛"),                 // Soft gold
            "Filling" => ("#E3F2FD", "#42A5F5", "●"),               // Soft blue
            "Implant" => ("#ECEFF1", "#607D8B", "⬡"),               // Slate
            "Periapical lesion" => ("#FFF3E0", "#FF9800", "◉"),     // Soft orange
            "Root Piece" => ("#EFEBE9", "#795548", "△"),             // Brown
            "Root canal obturation" => ("#E8F5E9", "#66BB6A", "⊕"), // Green
            "Missing teeth" => ("#FAFAFA", "#BDBDBD", "✕"),         // Gray
            "Deep Caries" => ("#FFCDD2", "#E53935", "⚠"),           // Deep red
            _ => ("#FFEBEE", "#EF5350", "⚠")
        };

        FillColor = new SolidColorBrush(Color.Parse(fill));
        StrokeColor = new SolidColorBrush(Color.Parse(stroke));
        StrokeThickness = 2.0;
        StatusIcon = icon;
        StatusIconColor = new SolidColorBrush(Color.Parse(stroke));
        TooltipText = $"#{FdiNumber}: {string.Join(", ", Pathologies)}";
    }
    
    public void MarkHealthy()
    {
         if (!HasPathology)
         {
             FillColor = new SolidColorBrush(Color.Parse("#E8F0FE"));
             StrokeColor = new SolidColorBrush(Color.Parse("#4CAF50"));
             StrokeThickness = 1.5;
             StatusIcon = "✓";
             StatusIconColor = new SolidColorBrush(Color.Parse("#4CAF50"));
         }
    }
    
    public void MarkTreatment(TreatmentItem treatment)
    {
        try
        {
            FillColor = Brush.Parse(treatment.Color);
        }
        catch
        {
            FillColor = Brushes.Gray;
        }
        
        StrokeThickness = 2.0;
        TooltipText = $"#{FdiNumber}: {treatment.Name} ({treatment.Category})";
    }
}
