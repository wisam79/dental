using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DentalID.Application.Services;

/// <summary>
/// Pathology detection inference.
/// Callers must hold <see cref="IOnnxSessionManager.InferenceLock"/> before calling.
/// </summary>
public sealed class PathologyDetectionService : IPathologyDetectionService
{
    private readonly IOnnxSessionManager      _session;
    private readonly IYoloDetectionParser     _yoloParser;
    private readonly ITensorPreparationService _tensorPrep;
    private readonly AiConfiguration          _config;
    private readonly AiSettings               _aiSettings;

    public PathologyDetectionService(
        IOnnxSessionManager       session,
        IYoloDetectionParser      yoloParser,
        ITensorPreparationService tensorPrep,
        AiConfiguration           config,
        AiSettings                aiSettings)
    {
        _session    = session    ?? throw new ArgumentNullException(nameof(session));
        _yoloParser = yoloParser ?? throw new ArgumentNullException(nameof(yoloParser));
        _tensorPrep = tensorPrep ?? throw new ArgumentNullException(nameof(tensorPrep));
        _config     = config     ?? throw new ArgumentNullException(nameof(config));
        _aiSettings = aiSettings ?? throw new ArgumentNullException(nameof(aiSettings));
    }

    public List<DetectedPathology> DetectPathologies(SKBitmap bitmap)
    {
        if (_session.PathologyDetector == null) return [];

        var (tensor, scale, padX, padY) = _tensorPrep.PrepareDetectionTensor(
            bitmap, _config.Model.DetectionInputSize, _session.DetectionBuffer);

        var inputs = new List<NamedOnnxValue>
            { NamedOnnxValue.CreateFromTensor(_session.PathologyInputName, tensor) };
        using var results = _session.PathologyDetector.Run(inputs);
        return _yoloParser.ParsePathologyDetections(
            results.First().AsTensor<float>(),
            _config.Model.DetectionInputSize,
            scale, padX, padY,
            _aiSettings.ConfidenceThreshold,
            _config.Model.PathologyClasses);
    }
}
