using DentalID.Core.DTOs;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DentalID.Application.Services;

/// <summary>
/// Parses YOLO model outputs into detection results.
/// Handles NMS, coordinate normalization, and pathology→tooth mapping.
/// Extracted from OnnxInferenceService for testability.
/// </summary>
public class YoloDetectionParser : IYoloDetectionParser
{
    private readonly AiConfiguration _config;
    private readonly AiSettings _aiSettings;
    private readonly IFdiSpatialService _fdiService;

    public YoloDetectionParser(AiConfiguration config, AiSettings aiSettings, IFdiSpatialService fdiService)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _aiSettings = aiSettings ?? throw new ArgumentNullException(nameof(aiSettings));
        _fdiService = fdiService ?? throw new ArgumentNullException(nameof(fdiService));
    }

    public List<DetectedTooth> ParseTeethDetections(Tensor<float> output, int inputSize, float scale, 
        float padX, float padY, float confidenceThreshold)
    {
        var teeth = new List<DetectedTooth>();
        // Bug #10 Fix: Removed debug Console.WriteLine calls that polluted production output.
        var dims = output.Dimensions;
        if (dims.Length < 3) return teeth;

        int numPredictions = dims[2];
        int numChannels = dims[1];
        int numClasses = numChannels - 4;

        for (int i = 0; i < numPredictions; i++)
        {
            float maxConf = 0;
            int bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float conf = output[0, c + 4, i];
                if (conf > maxConf) { maxConf = conf; bestClass = c; }
            }

            if (maxConf < confidenceThreshold) continue;

            float w = output[0, 2, i], h = output[0, 3, i];
            float x_canvas = output[0, 0, i] - w / 2, y_canvas = output[0, 1, i] - h / 2;
            float validWidth = inputSize - 2 * padX, validHeight = inputSize - 2 * padY;
            float x_norm = (x_canvas - padX) / validWidth, y_norm = (y_canvas - padY) / validHeight;
            float w_norm = w / validWidth, h_norm = h / validHeight;

            if (x_norm < -0.1 || y_norm < -0.1 || x_norm > 1.1 || y_norm > 1.1) continue;

            teeth.Add(new DetectedTooth
            {
                FdiNumber = bestClass < _config.FdiMapping.ClassMap.Length ? _config.FdiMapping.ClassMap[bestClass] : 0,
                Confidence = maxConf,
                X = Math.Clamp(x_norm, 0, 1), Y = Math.Clamp(y_norm, 0, 1),
                Width = Math.Clamp(w_norm, 0, 1), Height = Math.Clamp(h_norm, 0, 1)
            });
        }
        
        teeth = teeth.Where(t => t.FdiNumber != 0).ToList();
        var nmsTeeth = ApplyNms(teeth, _aiSettings.IouThreshold);
        return _fdiService.RefineFdiNumbering(nmsTeeth);
    }

    public List<DetectedPathology> ParsePathologyDetections(Tensor<float> output, int inputSize, float scale,
        float padX, float padY, float confidenceThreshold, string[] classNames)
    {
        var pathologies = new List<DetectedPathology>();
        var dims = output.Dimensions;
        if (dims.Length < 3) return pathologies;

        int numPredictions = dims[2];
        int numChannels = dims[1];
        int numClasses = Math.Min(numChannels - 4, classNames.Length);

        for (int i = 0; i < numPredictions; i++)
        {
            float maxConf = 0;
            int bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float conf = output[0, c + 4, i];
                if (conf > maxConf) { maxConf = conf; bestClass = c; }
            }

            string className = classNames[bestClass];
            float threshold = _config.Thresholds.PathologyThresholds.TryGetValue(className, out float t) ? t : confidenceThreshold;
            if (maxConf < threshold) continue;

            float w = output[0, 2, i], h = output[0, 3, i];
            float x_canvas = output[0, 0, i] - w / 2, y_canvas = output[0, 1, i] - h / 2;
            float validWidth = inputSize - 2 * padX, validHeight = inputSize - 2 * padY;

            pathologies.Add(new DetectedPathology
            {
                ClassName = className, Confidence = maxConf,
                X = Math.Clamp((x_canvas - padX) / validWidth, 0, 1),
                Y = Math.Clamp((y_canvas - padY) / validHeight, 0, 1),
                Width = Math.Clamp(w / validWidth, 0, 1),
                Height = Math.Clamp(h / validHeight, 0, 1)
            });
        }
        return ApplyNms(pathologies, _aiSettings.IouThreshold);
    }

    public void MapPathologiesToTeeth(List<DetectedTooth> teeth, List<DetectedPathology> pathologies)
    {
        foreach (var pathology in pathologies)
        {
            DetectedTooth? bestTooth = null;
            float maxIntersectionArea = 0;

            foreach (var tooth in teeth)
            {
                float left = Math.Max(pathology.X, tooth.X);
                float top = Math.Max(pathology.Y, tooth.Y);
                float right = Math.Min(pathology.X + pathology.Width, tooth.X + tooth.Width);
                float bottom = Math.Min(pathology.Y + pathology.Height, tooth.Y + tooth.Height);

                float width = Math.Max(0, right - left);
                float height = Math.Max(0, bottom - top);
                float area = width * height;

                if (area > 0 && area > maxIntersectionArea)
                {
                    maxIntersectionArea = area;
                    bestTooth = tooth;
                }
            }

            if (bestTooth == null)
            {
                float minDistance = float.MaxValue;
                float proximityThreshold = _config.Thresholds.ProximityThreshold;

                float pCenterX = pathology.X + pathology.Width / 2;
                float pCenterY = pathology.Y + pathology.Height / 2;

                foreach (var tooth in teeth)
                {
                    float tCenterX = tooth.X + tooth.Width / 2;
                    float tCenterY = tooth.Y + tooth.Height / 2;
                    float dist = (float)Math.Sqrt(Math.Pow(pCenterX - tCenterX, 2) + Math.Pow(pCenterY - tCenterY, 2));

                    if (dist < minDistance && dist < proximityThreshold)
                    {
                        minDistance = dist;
                        bestTooth = tooth;
                    }
                }
            }

            if (bestTooth != null)
            {
                pathology.ToothNumber = bestTooth.FdiNumber;
            }
        }
    }

    public List<T> ApplyNms<T>(List<T> detections, float iouThreshold) where T : class
    {
        if (detections.Count == 0) return detections;
        
        // Use reflection-free approach: extract box info based on known types
        Func<T, (float x, float y, float w, float h, float conf)> getter;
        if (typeof(T) == typeof(DetectedTooth))
        {
            getter = d => { var t = (d as DetectedTooth)!; return (t.X, t.Y, t.Width, t.Height, t.Confidence); };
        }
        else if (typeof(T) == typeof(DetectedPathology))
        {
            getter = d => { var p = (d as DetectedPathology)!; return (p.X, p.Y, p.Width, p.Height, p.Confidence); };
        }
        else
        {
            return detections; // Unknown type, return as-is
        }

        var sorted = detections.OrderByDescending(d => getter(d).conf).ToList();
        var results = new List<T>();
        var suppressed = new bool[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i]) continue;
            var current = sorted[i];
            results.Add(current);
            var c = getter(current);
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed[j]) continue;
                var o = getter(sorted[j]);
                if (ForensicHeuristicsService.CalculateIoU(c.x, c.y, c.w, c.h, o.x, o.y, o.w, o.h) > iouThreshold) 
                    suppressed[j] = true;
            }
        }
        return results;
    }
}
