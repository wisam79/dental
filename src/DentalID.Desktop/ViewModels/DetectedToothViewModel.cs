using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Core.DTOs;
using System;

namespace DentalID.Desktop.ViewModels;

public partial class DetectedToothViewModel : ViewModelBase
{
    public DetectedTooth Model { get; }

    [ObservableProperty]
    private string _formattedDetails;

    private readonly double _originalImageWidth;
    private readonly double _originalImageHeight;
    private readonly Action<DetectedToothViewModel> _onRequestEdit;

    public double CanvasLeft => Model.X * _originalImageWidth;
    public double CanvasTop => Model.Y * _originalImageHeight;
    public double CanvasWidth => Model.Width * _originalImageWidth;
    public double CanvasHeight => Model.Height * _originalImageHeight;

    public string FdiNumberDisplay => Model.FdiNumber.ToString();
    public string ConfidenceDisplay => $"{Model.Confidence:P0}";
    
    // Commands to interact (e.g., click to edit)
    [RelayCommand]
    private void Edit() => _onRequestEdit?.Invoke(this);

    public double X => CanvasLeft;
    public double Y => CanvasTop;
    public double Width => CanvasWidth;
    public double Height => CanvasHeight;
    public string TooltipText => FormattedDetails;
    public int FdiNumber => Model.FdiNumber;

    [RelayCommand]
    private void Delete() 
    {
        // No-op for now, or trigger an event if needed
    }

    public float Confidence => Model.Confidence;

    public DetectedToothViewModel(DetectedTooth model, double imageWidth, double imageHeight, Action<DetectedToothViewModel> onRequestEdit, string? detailsPrefix = null)
    {
        Model = model;
        _originalImageWidth = imageWidth;
        _originalImageHeight = imageHeight;
        _onRequestEdit = onRequestEdit;

        FormattedDetails = $"Tooth {Model.FdiNumber} ({Model.Confidence:P1})";
    }
}
