using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DentalID.Desktop.ViewModels;

namespace DentalID.Desktop.Views;

public partial class AnalysisLabView : UserControl
{
    private bool _isPanning;
    private Point _lastPanPoint;
    private Matrix _currentMatrix = Matrix.Identity;

    public AnalysisLabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnZoom(object? sender, PointerWheelEventArgs e)
    {
        var container = this.FindControl<Grid>("ZoomContainer");
        if (container == null) return;

        var point = e.GetPosition(container);
        var delta = e.Delta.Y > 0 ? 1.1 : 0.9;

        // Apply scale at point
        var matrix = _currentMatrix;
        
        // Translate to origin, scale, translate back
        // Matrix multiplication order is important:
        // 1. Translate point to (0,0)
        // 2. Scale
        // 3. Translate back
        
        // MatrixHelper.ScaleAt in Avalonia? Or manual?
        // Manual way:
        
        // Use Matrix.CreateScale(x, y)
        // But we want to scale around a point.
        // It's equivalent to Translate(-p) * Scale(s) * Translate(p)
        
        // Or simpler: just use Zoom/Pan logic directly on the Matrix.
        
        // Limit Zoom
        double currentScale = matrix.M11;
        if (currentScale * delta < 0.1 || currentScale * delta > 10.0) return;

        // Construct scale matrix around point
        // Note: Avalonia Matrix multiplication is typically Prepend by default?
        // Let's rely on standard matrix math.
        
        // 1. Translate so pivot is at (0,0) -> Offset by -point.X, -point.Y
        // 2. Scale
        // 3. Translate back
        
        // However, 'point' is relative to the transformed container. 
        // We should get position relative to the PARENT (the clip grid).
        var parent = container.Parent as Control;
        if (parent == null) return;
        
        var relativePoint = e.GetPosition(parent);
        
        // Translate -relativePoint
        var translateToOrigin = Matrix.CreateTranslation(-relativePoint.X, -relativePoint.Y);
        var scale = Matrix.CreateScale(delta, delta);
        var translateBack = Matrix.CreateTranslation(relativePoint.X, relativePoint.Y);
        
        // New Matrix = Old * (Translate * Scale * TranslateBack) -- NO
        // Correct is: Scale transformation applied to existing matrix.
        // Transformations accumulate.
        
        // M_new = M_old * ScaleAt(p)
        
        var scaleAt = Matrix.CreateTranslation(-relativePoint.X, -relativePoint.Y) * 
                      Matrix.CreateScale(delta, delta) * 
                      Matrix.CreateTranslation(relativePoint.X, relativePoint.Y);
        
        _currentMatrix = _currentMatrix * scaleAt;
        
        ApplyMatrix(container);
        e.Handled = true;
    }

    private void OnPanStart(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed || 
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            e.Pointer.Capture(this as Control);
        }
    }

    private void OnPanMove(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;
        
        var pointProps = e.GetCurrentPoint(this).Properties;
        if (!pointProps.IsLeftButtonPressed && !pointProps.IsMiddleButtonPressed)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            return;
        }

        var container = this.FindControl<Grid>("ZoomContainer");
        if (container == null) return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _lastPanPoint;
        _lastPanPoint = currentPoint;

        // Apply translation
        var translate = Matrix.CreateTranslation(delta.X, delta.Y);
        _currentMatrix = _currentMatrix * translate;
        
        ApplyMatrix(container);
    }

    private void OnPanEnd(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void OnResetZoom(object? sender, RoutedEventArgs e)
    {
        var container = this.FindControl<Grid>("ZoomContainer");
        if (container == null) return;

        // Bug #29 Fix: Only reset the view transform (zoom/pan matrix) here.
        // Do NOT call vm.ResetViewCommand — that command clears the loaded image AND
        // the analysis results, which is completely destructive and unrelated to "Reset Zoom".
        // Zoom is a view-layer concern only; the ViewModel state should not be touched here.
        _currentMatrix = Matrix.Identity;
        ApplyMatrix(container);
    }

    private void ApplyMatrix(Grid container)
    {
        if (container.RenderTransform is MatrixTransform mt)
        {
            mt.Matrix = _currentMatrix;
        }
        else
        {
            container.RenderTransform = new MatrixTransform(_currentMatrix);
        }
    }

}
