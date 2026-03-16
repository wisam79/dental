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
                if (string.IsNullOrWhiteSpace(_pathData)) return null;

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
                    catch
                    { 
                        // Bug Fix #79: Return null instead of throwing to prevent Avalonia render loop crash
                        return null; 
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
        if (string.IsNullOrWhiteSpace(pathology)) return;
        
        // Bug Fix #80: Deduplicate pathology strings per tooth
        if (Pathologies.Contains(pathology)) return;

        HasPathology = true;
        Pathologies.Add(pathology);
        
        // Forensic Color Coding
        var (fill, stroke, icon) = pathology switch
        {
            "Caries" => ("#FFEBEE", "#EF5350", "⚠"),
            "Crown" => ("#FFF8E1", "#FFC107", "♛"),
            "Filling" => ("#E3F2FD", "#42A5F5", "●"),
            "Implant" => ("#ECEFF1", "#607D8B", "⬡"),
            "Periapical lesion" => ("#FFF3E0", "#FF9800", "◉"),
            "Root Piece" => ("#EFEBE9", "#795548", "△"),
            "Root canal obturation" => ("#E8F5E9", "#66BB6A", "⊕"),
            "Missing teeth" => ("#FAFAFA", "#BDBDBD", "✕"),
            "Deep Caries" => ("#FFCDD2", "#E53935", "⚠"),
            _ => ("#FFEBEE", "#EF5350", "⚠")
        };

        if (pathology == "Missing teeth") IsPresent = false;

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
            // Bug Fix #81: Safe Color Parsing
            if (treatment.Color.StartsWith("#"))
                FillColor = new SolidColorBrush(Color.Parse(treatment.Color));
            else
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
