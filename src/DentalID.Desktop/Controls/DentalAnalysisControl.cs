using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DentalID.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Collections.Specialized;
using DentalID.Desktop.ViewModels;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.Controls;

public class DentalAnalysisControl : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<DentalAnalysisControl, IImage?>(nameof(Source));

    public static readonly StyledProperty<IEnumerable<DetectedTooth>?> TeethProperty =
        AvaloniaProperty.Register<DentalAnalysisControl, IEnumerable<DetectedTooth>?>(nameof(Teeth));

    public static readonly StyledProperty<IEnumerable<DetectedPathology>?> PathologiesProperty =
        AvaloniaProperty.Register<DentalAnalysisControl, IEnumerable<DetectedPathology>?>(nameof(Pathologies));

    public static readonly StyledProperty<bool> ShowTeethProperty =
        AvaloniaProperty.Register<DentalAnalysisControl, bool>(nameof(ShowTeeth), true);

    public static readonly StyledProperty<bool> ShowPathologiesProperty =
        AvaloniaProperty.Register<DentalAnalysisControl, bool>(nameof(ShowPathologies), true);

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IEnumerable<DetectedTooth>? Teeth
    {
        get => GetValue(TeethProperty);
        set => SetValue(TeethProperty, value);
    }

    public IEnumerable<DetectedPathology>? Pathologies
    {
        get => GetValue(PathologiesProperty);
        set => SetValue(PathologiesProperty, value);
    }

    public bool ShowTeeth
    {
        get => GetValue(ShowTeethProperty);
        set => SetValue(ShowTeethProperty, value);
    }

    public bool ShowPathologies
    {
        get => GetValue(ShowPathologiesProperty);
        set => SetValue(ShowPathologiesProperty, value);
    }

    public static readonly StyledProperty<ILocalizationService?> LocalizationServiceProperty =
        AvaloniaProperty.Register<DentalAnalysisControl, ILocalizationService?>(nameof(LocalizationService));

    public ILocalizationService? LocalizationService
    {
        get => GetValue(LocalizationServiceProperty);
        set => SetValue(LocalizationServiceProperty, value);
    }

    static DentalAnalysisControl()
    {
        AffectsRender<DentalAnalysisControl>(SourceProperty, TeethProperty, PathologiesProperty, ShowTeethProperty, ShowPathologiesProperty, LocalizationServiceProperty);
    }

    public DentalAnalysisControl()
    {
        // Constructor empty, logic moved to OnPropertyChanged
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LocalizationServiceProperty)
        {
            if (change.OldValue is ILocalizationService oldSvc)
                oldSvc.PropertyChanged -= OnLocChanged;

            if (change.NewValue is ILocalizationService newSvc)
                newSvc.PropertyChanged += OnLocChanged;

            InvalidateVisual();
        }
        else if (change.Property == TeethProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldList)
                oldList.CollectionChanged -= OnCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newList)
                newList.CollectionChanged += OnCollectionChanged;
            InvalidateVisual();
        }
        else if (change.Property == PathologiesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldList)
                oldList.CollectionChanged -= OnCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newList)
                newList.CollectionChanged += OnCollectionChanged;
            InvalidateVisual();
        }
        if (change.Property == ShowTeethProperty || change.Property == ShowPathologiesProperty)
        {
            InvalidateVisual();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual);
    }

    private void OnLocChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual);
    }

        private IBrush GetBrush(string key, IBrush fallback)
        {
            if (Avalonia.Application.Current != null && Avalonia.Application.Current.TryFindResource(key, out var resource) && resource is IBrush brush)
            {
                return brush;
            }
            return fallback;
        }

        public override void Render(DrawingContext context)
        {
            var loc = LocalizationService ?? Loc.Instance; // Fallback to singleton if not bound
            bool isRtl = loc.IsRtl;

            var source = Source;
            if (source == null)
            {
                // Draw empty state
                var controlBounds = Bounds;
                var text = new FormattedText(
                    loc["Control_NoImage"],
                    CultureInfo.CurrentCulture,
                    isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    Typeface.Default,
                    14,
                    GetBrush("TextMutedBrush", Brushes.Gray)
                );
                var textX = (controlBounds.Width - text.Width) / 2;
                var textY = (controlBounds.Height - text.Height) / 2;
                context.DrawText(text, new Point(textX, textY));
                return;
            }

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // Calculate aspect-ratio preserving rect
            var srcSize = source.Size;
            var scale = Math.Min(bounds.Width / srcSize.Width, bounds.Height / srcSize.Height);
            var destWidth = srcSize.Width * scale;
            var destHeight = srcSize.Height * scale;
            var destX = (bounds.Width - destWidth) / 2;
            var destY = (bounds.Height - destHeight) / 2;
            var destRect = new Rect(destX, destY, destWidth, destHeight);

            // Draw Image
            context.DrawImage(source, destRect);

            // Draw Overlays
            if (ShowTeeth && Teeth != null)
            {
                var brush = GetBrush("AccentSecondaryBrush", Brushes.Cyan);
                var toothPen = new Pen(brush, 2);
                var textBrush = GetBrush("TextOnAccentBrush", Brushes.White);
                
                // Semi-transparent background for label
                var bgBrush = new SolidColorBrush(Colors.Black, 0.5); 
                if (brush is ISolidColorBrush scb) 
                    bgBrush = new SolidColorBrush(scb.Color, 0.6);
                
                foreach (var tooth in Teeth)
                {
                    // Validate coordinates
                    if (tooth.X < 0 || tooth.X > 1 || tooth.Y < 0 || tooth.Y > 1 ||
                        tooth.Width <= 0 || tooth.Width > 1 || tooth.Height <= 0 || tooth.Height > 1)
                        continue;

                    var x = destRect.X + tooth.X * destRect.Width;
                    var y = destRect.Y + tooth.Y * destRect.Height;
                    var w = tooth.Width * destRect.Width;
                    var h = tooth.Height * destRect.Height;

                    var rect = new Rect(x, y, w, h);
                    context.DrawRectangle(null, toothPen, rect);

                    // Draw Label with bounds checking
                    var label = tooth.FdiNumber.ToString();
                    var formattedText = new FormattedText(
                        label,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        Typeface.Default,
                        12,
                        textBrush
                    );

                    var labelX = x;
                    var labelY = y - 16;

                    // Ensure label stays within control bounds
                    if (labelY < 0) labelY = y + h;
                    if (labelX < bounds.Left) labelX = bounds.Left;
                    if (labelX + formattedText.Width + 8 > bounds.Right) 
                        labelX = bounds.Right - formattedText.Width - 8;

                    var labelRect = new Rect(labelX, labelY, formattedText.Width + 8, 16);
                    context.DrawRectangle(bgBrush, null, labelRect);
                    context.DrawText(formattedText, new Point(labelX + 4, labelY));
                }
            }

            if (ShowPathologies && Pathologies != null)
            {
                var textBrush = GetBrush("TextOnAccentBrush", Brushes.White);
                
                // Optimize: Group by class to reduce Pen/Brush creation similar to batching
                var groups = Pathologies.GroupBy(p => p.ClassName.ToLower());

                foreach (var group in groups)
                {
                    // Resolve Pen/Brush once per group
                    var className = group.Key;
                    var colorBrush = className switch
                    {
                        "caries" => GetBrush("ErrorBrush", Brushes.Red),
                        "crown" => GetBrush("WarningBrush", Brushes.Gold),
                        "filling" => GetBrush("TextDisabledBrush", Brushes.Silver),
                        "root_canal" => GetBrush("AccentBlueDarkBrush", Brushes.Purple),
                        _ => GetBrush("AccentTertiaryBrush", Brushes.Orange)
                    };

                    var pathPen = new Pen(colorBrush, 2);
                    var bgBrush = new SolidColorBrush(Colors.Red, 0.6);
                    if (colorBrush is ISolidColorBrush scb)
                        bgBrush = new SolidColorBrush(scb.Color, 0.6);

                    foreach (var path in group)
                    {
                         // Validate coordinates
                        if (path.X < 0 || path.X > 1 || path.Y < 0 || path.Y > 1 ||
                            path.Width <= 0 || path.Width > 1 || path.Height <= 0 || path.Height > 1)
                            continue;

                        var x = destRect.X + path.X * destRect.Width;
                        var y = destRect.Y + path.Y * destRect.Height;
                        var w = path.Width * destRect.Width;
                        var h = path.Height * destRect.Height;

                        var rect = new Rect(x, y, w, h);
                        context.DrawRectangle(null, pathPen, rect);

                        // Localized Label
                        var labelKey = $"Pathology_{path.ClassName}";
                        var label = loc[labelKey];
                        if (label.StartsWith("[")) label = path.ClassName; 

                        var formattedText = new FormattedText(
                            label,
                            CultureInfo.CurrentCulture,
                            isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                            Typeface.Default,
                            11,
                            textBrush
                        );

                        var labelX = isRtl ? x + w - formattedText.Width - 8 : x;
                        var labelY = y + h;

                        // Ensure label stays within control bounds
                        if (labelY + 16 > bounds.Bottom) labelY = y - 16;
                        if (labelX < bounds.Left) labelX = bounds.Left;
                        if (labelX + formattedText.Width + 8 > bounds.Right) 
                            labelX = bounds.Right - formattedText.Width - 8;

                        var labelRect = new Rect(labelX, labelY, formattedText.Width + 8, 16);
                        context.DrawRectangle(bgBrush, null, labelRect);
                        context.DrawText(formattedText, new Point(labelX + 4, labelY));
                    }
                }
            }
        }
}
