using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DentalID.Application.Services;

/// <summary>
/// All teeth-detection inference logic: standard run, TTA augmentation,
/// edge-crop rescue, NMS finalisation, and detection merging.
/// Callers must hold <see cref="IOnnxSessionManager.InferenceLock"/> before calling any method.
/// </summary>
public sealed class TeethDetectionService : ITeethDetectionService
{
    private const float EdgeRescueCropFraction  = 0.75f;
    private const float EdgeRescueMinAspectRatio = 1.2f;

    private readonly IOnnxSessionManager       _session;
    private readonly IYoloDetectionParser      _yoloParser;
    private readonly IFdiSpatialService        _fdiService;
    private readonly IForensicHeuristicsService _heuristics;
    private readonly ITensorPreparationService  _tensorPrep;
    private readonly AiConfiguration           _config;
    private readonly AiSettings                _aiSettings;

    public TeethDetectionService(
        IOnnxSessionManager        session,
        IYoloDetectionParser       yoloParser,
        IFdiSpatialService         fdiService,
        IForensicHeuristicsService heuristics,
        ITensorPreparationService  tensorPrep,
        AiConfiguration            config,
        AiSettings                 aiSettings)
    {
        _session     = session     ?? throw new ArgumentNullException(nameof(session));
        _yoloParser  = yoloParser  ?? throw new ArgumentNullException(nameof(yoloParser));
        _fdiService  = fdiService  ?? throw new ArgumentNullException(nameof(fdiService));
        _heuristics  = heuristics  ?? throw new ArgumentNullException(nameof(heuristics));
        _tensorPrep  = tensorPrep  ?? throw new ArgumentNullException(nameof(tensorPrep));
        _config      = config      ?? throw new ArgumentNullException(nameof(config));
        _aiSettings  = aiSettings  ?? throw new ArgumentNullException(nameof(aiSettings));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Standard detection
    // ─────────────────────────────────────────────────────────────────────────

    public List<DetectedTooth> DetectTeeth(SKBitmap bitmap)
    {
        if (_session.TeethDetector == null) return [];
        var (tensor, scale, padX, padY) = _tensorPrep.PrepareDetectionTensor(
            bitmap, _config.Model.DetectionInputSize, _session.DetectionBuffer);

        var inputs = new List<NamedOnnxValue>
            { NamedOnnxValue.CreateFromTensor(_session.TeethInputName, tensor) };
        using var results = _session.TeethDetector.Run(inputs);
        return _yoloParser.ParseTeethDetections(
            results.First().AsTensor<float>(),
            _config.Model.DetectionInputSize,
            scale, padX, padY,
            _aiSettings.ConfidenceThreshold);
    }

    public List<DetectedTooth> DetectTeethOnBitmap(SKBitmap bitmap, float confidenceThreshold)
    {
        if (_session.TeethDetector == null) return [];
        var (tensor, scale, padX, padY) = _tensorPrep.PrepareDetectionTensor(
            bitmap, _config.Model.DetectionInputSize, _session.TtaDetectionBuffer);

        var inputs = new List<NamedOnnxValue>
            { NamedOnnxValue.CreateFromTensor(_session.TeethInputName, tensor) };
        using var results = _session.TeethDetector.Run(inputs);
        return _yoloParser.ParseTeethDetections(
            results.First().AsTensor<float>(),
            _config.Model.DetectionInputSize,
            scale, padX, padY,
            confidenceThreshold);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DetectObjects — both models (used by TTA)
    // ─────────────────────────────────────────────────────────────────────────

    public (List<DetectedTooth> Teeth, List<DetectedPathology> Pathologies) DetectObjects(SKBitmap bitmap)
    {
        var (tensor, scale, padX, padY) = _tensorPrep.PrepareDetectionTensor(
            bitmap, _config.Model.DetectionInputSize, _session.TtaDetectionBuffer);

        var teeth = new List<DetectedTooth>();
        var pathologies = new List<DetectedPathology>();

        if (_session.TeethDetector != null)
        {
            var inputs = new List<NamedOnnxValue>
                { NamedOnnxValue.CreateFromTensor(_session.TeethInputName, tensor) };
            using var r = _session.TeethDetector.Run(inputs);
            teeth = _yoloParser.ParseTeethDetections(
                r.First().AsTensor<float>(),
                _config.Model.DetectionInputSize, scale, padX, padY,
                _aiSettings.ConfidenceThreshold);
        }

        if (_session.PathologyDetector != null)
        {
            var inputs = new List<NamedOnnxValue>
                { NamedOnnxValue.CreateFromTensor(_session.PathologyInputName, tensor) };
            using var r = _session.PathologyDetector.Run(inputs);
            pathologies = _yoloParser.ParsePathologyDetections(
                r.First().AsTensor<float>(),
                _config.Model.DetectionInputSize, scale, padX, padY,
                _aiSettings.ConfidenceThreshold,
                _config.Model.PathologyClasses);
        }

        return (teeth, pathologies);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TTA (Test-Time Augmentation)
    // ─────────────────────────────────────────────────────────────────────────

    public void ApplyTta(AnalysisResult result, SKBitmap original)
    {
        result.RawTeeth ??= new List<DetectedTooth>();
        result.RawPathologies ??= new List<DetectedPathology>();

        using var flipped = new SKBitmap(original.Width, original.Height);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Clear();
            canvas.Scale(-1, 1, original.Width / 2.0f, original.Height / 2.0f);
            canvas.DrawBitmap(original, 0, 0);
        }

        var (flippedTeeth, flippedPaths) = DetectObjects(flipped);

        // Mirror coordinates back to original space
        foreach (var t in flippedTeeth)  
        {
            t.X = 1.0f - (t.X + t.Width);
            // Bug Fix: YOLO detects flipped anatomical positions, so we must mirror the FDI number back!
            if (t.FdiNumber > 0)
            {
                int quad = t.FdiNumber / 10;
                int tooth = t.FdiNumber % 10;
                int newQuad = quad switch
                {
                    1 => 2, 2 => 1,
                    3 => 4, 4 => 3,
                    5 => 6, 6 => 5,
                    7 => 8, 8 => 7,
                    _ => quad
                };
                t.FdiNumber = newQuad * 10 + tooth;
            }
        }
        foreach (var p in flippedPaths)  p.X = 1.0f - (p.X + p.Width);

        MergeDetections(result.RawTeeth, flippedTeeth);

        var mergedPaths = new List<DetectedPathology>(result.RawPathologies);
        MergePathologies(mergedPaths, flippedPaths);

        result.Teeth = BuildFinalTeeth(result.RawTeeth);
        result.RawPathologies = mergedPaths;
        result.Pathologies = new List<DetectedPathology>(mergedPaths);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edge-crop rescue
    // ─────────────────────────────────────────────────────────────────────────

    public bool ShouldApplyEdgeCropRescue(SKBitmap bitmap, List<DetectedTooth> teeth)
    {
        if (_session.TeethDetector == null || bitmap.Width <= 0 || bitmap.Height <= 0 || teeth.Count == 0)
            return false;

        float aspect = (float)bitmap.Width / bitmap.Height;
        if (aspect < EdgeRescueMinAspectRatio) return false;

        var uniqueFdi = teeth.Where(t => t.FdiNumber > 0).Select(t => t.FdiNumber).Distinct().ToList();
        if (uniqueFdi.Count >= 32) return false;

        var edgeFdi = new HashSet<int> { 18, 28, 38, 48 };
        return uniqueFdi.Count(edgeFdi.Contains) < edgeFdi.Count;
    }

    public void ApplyEdgeCropRescue(AnalysisResult result, SKBitmap original)
    {
        if (_session.TeethDetector == null) return;

        int cropWidth = (int)Math.Round(original.Width * EdgeRescueCropFraction);
        if (cropWidth <= 0 || cropWidth >= original.Width) return;

        float threshold = Math.Max(0.18f,
            Math.Min(_config.Thresholds.TeethThreshold * 0.95f, _aiSettings.ConfidenceThreshold * 0.75f));
        float cropFraction = cropWidth / (float)original.Width;

        foreach (int startX in new[] { 0, original.Width - cropWidth }.Distinct())
        {
            using var crop = new SKBitmap(cropWidth, original.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(crop))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(original,
                    new SKRect(startX, 0, startX + cropWidth, original.Height),
                    new SKRect(0, 0, cropWidth, original.Height));
            }

            float offsetX = startX / (float)original.Width;
            foreach (var t in DetectTeethOnBitmap(crop, threshold))
            {
                t.X      = Math.Clamp(offsetX + t.X * cropFraction, 0f, 1f);
                t.Width  = Math.Clamp(t.Width * cropFraction, 0f, 1f);
                t.Y      = Math.Clamp(t.Y,      0f, 1f);
                t.Height = Math.Clamp(t.Height, 0f, 1f);
                result.RawTeeth.Add(t);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NMS + FDI finalisation
    // ─────────────────────────────────────────────────────────────────────────

    public List<DetectedTooth> BuildFinalTeeth(List<DetectedTooth> candidates)
    {
        var teeth = _yoloParser.ApplyNms(candidates, _aiSettings.IouThreshold)
            .Where(t => t.FdiNumber > 0)
            .GroupBy(t => t.FdiNumber)
            .Select(g => g.OrderByDescending(t => t.Confidence).First())
            .OrderByDescending(t => t.Confidence)
            .Take(32)
            .ToList();

        if (_config.FdiMapping.ClassMap.Length < 28)
        {
            teeth = _fdiService.RefineFdiNumbering(teeth)
                .Where(t => t.FdiNumber > 0)
                .GroupBy(t => t.FdiNumber)
                .Select(g => g.OrderByDescending(t => t.Confidence).First())
                .OrderByDescending(t => t.Confidence)
                .Take(32)
                .ToList();
        }
        return teeth;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Merge helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void MergeDetections(List<DetectedTooth> original, List<DetectedTooth> augmented)
    {
        float addThreshold = Math.Max(0.18f,
            Math.Min(_config.Thresholds.TeethThreshold, _aiSettings.ConfidenceThreshold * 0.65f));

        foreach (var aug in augmented)
        {
            var match = original.FirstOrDefault(o =>
                ForensicHeuristicsService.CalculateIoU(o.X, o.Y, o.Width, o.Height,
                                                       aug.X, aug.Y, aug.Width, aug.Height)
                > _aiSettings.IouThreshold);

            if (match != null)
            {
                match.Confidence = Math.Min(0.99f, match.Confidence + 0.15f);
                match.X      = (match.X + aug.X) / 2;
                match.Y      = (match.Y + aug.Y) / 2;
                match.Width  = (match.Width  + aug.Width)  / 2;
                match.Height = (match.Height + aug.Height) / 2;
            }
            else if (aug.Confidence >= addThreshold)
            {
                original.Add(aug);
            }
        }
    }

    private void MergePathologies(List<DetectedPathology> original, List<DetectedPathology> augmented)
    {
        foreach (var aug in augmented)
        {
            var match = original.FirstOrDefault(o =>
                o.ClassName == aug.ClassName &&
                ForensicHeuristicsService.CalculateIoU(o.X, o.Y, o.Width, o.Height,
                                                       aug.X, aug.Y, aug.Width, aug.Height) > 0.3f);
            if (match != null)
                match.Confidence = Math.Min(0.99f, match.Confidence + 0.10f);
            else if (aug.Confidence > _aiSettings.ConfidenceThreshold)
                original.Add(aug);
        }
    }
}
