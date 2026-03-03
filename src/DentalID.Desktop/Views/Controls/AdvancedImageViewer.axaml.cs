using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DentalID.Desktop.Views.Controls;

public partial class AdvancedImageViewer : UserControl
{
    private bool _isPanning;
    private Point _startPoint;
    private Point _startOffset;
    private double _zoom = 1.0;

    public static readonly StyledProperty<object?> InnerContentProperty =
        AvaloniaProperty.Register<AdvancedImageViewer, object?>(nameof(InnerContent));

    public object? InnerContent
    {
        get => GetValue(InnerContentProperty);
        set => SetValue(InnerContentProperty, value);
    }

    public AdvancedImageViewer()
    {
        InitializeComponent();
        
        TransformParent.RenderTransform = new TransformGroup
        {
            Children = new Transforms
            {
                new ScaleTransform(),
                new TranslateTransform()
            }
        };
    }

    private TransformGroup GetTransformGroup() => (TransformGroup)TransformParent.RenderTransform!;
    private ScaleTransform GetScale() => (ScaleTransform)GetTransformGroup().Children[0];
    private TranslateTransform GetTranslate() => (TranslateTransform)GetTransformGroup().Children[1];

    public void ResetTransform()
    {
        _zoom = 1.0;
        var st = GetScale();
        st.ScaleX = 1.0;
        st.ScaleY = 1.0;
        var tt = GetTranslate();
        tt.X = 0;
        tt.Y = 0;
    }

    private void UpdateZoom(double delta, Point centerPoint)
    {
        var st = GetScale();
        var tt = GetTranslate();

        double prevZoom = _zoom;
        _zoom += delta * _zoom;
        _zoom = Math.Clamp(_zoom, 0.1, 50.0);

        double unscaledX = (centerPoint.X - tt.X) / prevZoom;
        double unscaledY = (centerPoint.Y - tt.Y) / prevZoom;

        tt.X = centerPoint.X - (unscaledX * _zoom);
        tt.Y = centerPoint.Y - (unscaledY * _zoom);

        st.ScaleX = _zoom;
        st.ScaleY = _zoom;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(ViewPort);
        double delta = e.Delta.Y > 0 ? 0.15 : -0.15;
        UpdateZoom(delta, pos);
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewPort);
        if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _startPoint = point.Position;
            var tt = GetTranslate();
            _startOffset = new Point(tt.X, tt.Y);
            
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursor.Default;
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning)
        {
            var currentPoint = e.GetPosition(ViewPort);
            var offsetX = currentPoint.X - _startPoint.X;
            var offsetY = currentPoint.Y - _startPoint.Y;

            var tt = GetTranslate();
            tt.X = _startOffset.X + offsetX;
            tt.Y = _startOffset.Y + offsetY;

            e.Handled = true;
        }
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        var center = new Point(ViewPort.Bounds.Width / 2, ViewPort.Bounds.Height / 2);
        UpdateZoom(0.2, center);
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        var center = new Point(ViewPort.Bounds.Width / 2, ViewPort.Bounds.Height / 2);
        UpdateZoom(-0.2, center);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        ResetTransform();
    }
}
