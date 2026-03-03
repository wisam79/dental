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
    private const int MaxAdultTeeth = 52;
    private const int CoverageTargetTeeth = 28;
    private const int MinDirectClassMapLengthForTrustedFdi = 28;
    private const float CoverageClassConfidenceFloorSparse = 0.08f;
    private const float CoverageClassConfidenceFloorDense = 0.20f;
    private const float MinToothArea = 0.00018f;
    private const float MaxToothArea = 0.08f;
    private const float MinToothAspectRatio = 0.25f;
    private const float MaxToothAspectRatio = 2.8f;
    private const float MinNormalizedWidth = 0.009f;
    private const float MinNormalizedHeight = 0.015f;
    private const float RescueMinToothArea = 0.00008f;
    private const float RescueMaxToothArea = 0.12f;
    private const float RescueMinAspectRatio = 0.08f;
    private const float RescueMaxAspectRatio = 5.2f;
    private const float RescueMinWidth = 0.006f;
    private const float RescueMinHeight = 0.010f;
    private const int SpatialRescueMinCandidates = 12;
    private const float MinPathologyWidth = 0.006f;
    private const float MinPathologyHeight = 0.006f;
    private const float MaxPathologyArea = 0.16f;
    private const float MaxMissingToothArea = 0.24f;
    private const float MinPathologyOverlapRatio = 0.25f;
    private const float PathologyClassMarginMin = 0.06f;
    private const float PathologyMarginHighConfidenceBypass = 0.78f;

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
        // Bug #10 Fix: Removed debug Console.WriteLine calls that polluted production output.
        var dims = output.Dimensions;
        if (dims.Length < 3) return new List<DetectedTooth>();

        int numPredictions = dims[2];
        int numChannels = dims[1];
        int numClasses = numChannels - 4;
        bool applySigmoidScores = ShouldApplySigmoidScores(output, numPredictions, numClasses);
        var strictThreshold = Math.Max(_config.Thresholds.TeethThreshold, confidenceThreshold * 0.70f);
        var rescueThreshold = Math.Max(0.18f, Math.Min(_config.Thresholds.TeethThreshold * 0.85f, confidenceThreshold * 0.55f));
        bool hasDirectFdiClassMap = _config.FdiMapping.ClassMap.Length >= MinDirectClassMapLengthForTrustedFdi;
        bool shouldUseSpatialRefine = !hasDirectFdiClassMap;

        var strictCandidates = ParseToothCandidates(output, inputSize, padX, padY, numPredictions, numClasses, strictThreshold, strictMode: true, applySigmoidScores);
        var strictFinal = FinalizeTeeth(
            strictCandidates,
            preferSpatialRefine: shouldUseSpatialRefine && strictCandidates.Count >= 20,
            iouThreshold: Math.Min(_aiSettings.IouThreshold, 0.36f));
        if (strictFinal.Count >= 20)
        {
            return ApplyCoverageSupplementIfNeeded(
                output,
                inputSize,
                padX,
                padY,
                numPredictions,
                numClasses,
                strictFinal,
                applySigmoidScores);
        }

        // Rescue mode: when strict pass under-detects, relax geometry and confidence.
        var rescueCandidates = ParseToothCandidates(output, inputSize, padX, padY, numPredictions, numClasses, rescueThreshold, strictMode: false, applySigmoidScores);
        var mergedCandidates = strictCandidates
            .Concat(rescueCandidates)
            .OrderByDescending(t => t.Confidence)
            .ToList();
        var rescueFinal = FinalizeTeeth(
            mergedCandidates,
            preferSpatialRefine: shouldUseSpatialRefine && mergedCandidates.Count >= 22,
            iouThreshold: Math.Min(_aiSettings.IouThreshold, 0.42f));
        return ApplyCoverageSupplementIfNeeded(
            output,
            inputSize,
            padX,
            padY,
            numPredictions,
            numClasses,
            rescueFinal,
            applySigmoidScores);
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
        bool applySigmoidScores = ShouldApplySigmoidScores(output, numPredictions, numClasses);
        float validWidth = inputSize - 2 * padX;
        float validHeight = inputSize - 2 * padY;

        for (int i = 0; i < numPredictions; i++)
        {
            float maxConf = 0;
            float secondConf = 0;
            int bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float conf = NormalizeClassScore(output[0, c + 4, i], applySigmoidScores);
                if (conf > maxConf)
                {
                    secondConf = maxConf;
                    maxConf = conf;
                    bestClass = c;
                }
                else if (conf > secondConf)
                {
                    secondConf = conf;
                }
            }

            string className = classNames[bestClass];
            float threshold = _config.Thresholds.PathologyThresholds.TryGetValue(className, out float t) ? t : confidenceThreshold;
            if (maxConf < threshold) continue;
            if (!IsReliablePathologyClassDecision(maxConf, secondConf, threshold))
                continue;

            float w = output[0, 2, i], h = output[0, 3, i];
            float x_canvas = output[0, 0, i] - w / 2;
            float y_canvas = output[0, 1, i] - h / 2;
            float xNorm = (x_canvas - padX) / validWidth;
            float yNorm = (y_canvas - padY) / validHeight;
            float wNorm = w / validWidth;
            float hNorm = h / validHeight;

            if (xNorm < -0.1f || yNorm < -0.1f || xNorm > 1.1f || yNorm > 1.1f)
                continue;
            if (!IsPlausiblePathologyBox(className, wNorm, hNorm))
                continue;

            pathologies.Add(new DetectedPathology
            {
                ClassName = className, Confidence = maxConf,
                X = Math.Clamp(xNorm, 0, 1),
                Y = Math.Clamp(yNorm, 0, 1),
                Width = Math.Clamp(wNorm, 0, 1),
                Height = Math.Clamp(hNorm, 0, 1)
            });
        }

        var nms = ApplyNms(pathologies, Math.Min(_aiSettings.IouThreshold, 0.35f));
        return nms
            .GroupBy(p => p.ClassName)
            .SelectMany(g => g
                .OrderByDescending(p => p.Confidence)
                .Take(GetPathologyPerClassCap(g.Key)))
            .OrderByDescending(p => p.Confidence)
            .Take(96)
            .ToList();
    }

    public void MapPathologiesToTeeth(List<DetectedTooth> teeth, List<DetectedPathology> pathologies)
    {
        foreach (var pathology in pathologies)
        {
            DetectedTooth? bestTooth = null;
            float bestScore = 0f;
            float pathologyArea = Math.Max(pathology.Width * pathology.Height, 1e-6f);
            float pCenterX = pathology.X + pathology.Width / 2;
            float pCenterY = pathology.Y + pathology.Height / 2;

            foreach (var tooth in teeth)
            {
                float left = Math.Max(pathology.X, tooth.X);
                float top = Math.Max(pathology.Y, tooth.Y);
                float right = Math.Min(pathology.X + pathology.Width, tooth.X + tooth.Width);
                float bottom = Math.Min(pathology.Y + pathology.Height, tooth.Y + tooth.Height);

                float width = Math.Max(0, right - left);
                float height = Math.Max(0, bottom - top);
                float area = width * height;
                float overlapRatio = area / pathologyArea;
                bool centerInside = IsPointInside(tooth.X, tooth.Y, tooth.Width, tooth.Height, pCenterX, pCenterY);

                if (!centerInside && overlapRatio < MinPathologyOverlapRatio)
                    continue;

                // Prefer center containment + stronger overlap
                float score = overlapRatio + (centerInside ? 0.25f : 0f) + (tooth.Confidence * 0.05f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTooth = tooth;
                }
            }

            if (bestTooth == null && AllowProximityFallback(pathology.ClassName))
            {
                float minDistance = float.MaxValue;
                float proximityThreshold = GetPathologyProximityThreshold(pathology.ClassName, _config.Thresholds.ProximityThreshold);

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

    private List<DetectedTooth> ParseToothCandidates(
        Tensor<float> output,
        int inputSize,
        float padX,
        float padY,
        int numPredictions,
        int numClasses,
        float threshold,
        bool strictMode,
        bool applySigmoidScores)
    {
        var teeth = new List<DetectedTooth>();
        for (int i = 0; i < numPredictions; i++)
        {
            float maxConf = 0;
            int bestClass = 0;
            for (int c = 0; c < numClasses; c++)
            {
                float conf = NormalizeClassScore(output[0, c + 4, i], applySigmoidScores);
                if (conf > maxConf) { maxConf = conf; bestClass = c; }
            }

            if (maxConf < threshold) continue;

            float w = output[0, 2, i], h = output[0, 3, i];
            float x_canvas = output[0, 0, i] - w / 2;
            float y_canvas = output[0, 1, i] - h / 2;
            float validWidth = inputSize - 2 * padX;
            float validHeight = inputSize - 2 * padY;
            float x_norm = (x_canvas - padX) / validWidth;
            float y_norm = (y_canvas - padY) / validHeight;
            float w_norm = w / validWidth;
            float h_norm = h / validHeight;

            if (x_norm < -0.1 || y_norm < -0.1 || x_norm > 1.1 || y_norm > 1.1) continue;
            if (!IsPlausibleToothBox(w_norm, h_norm, strictMode)) continue;

            int mappedFdi = MapClassIndexToFdi(bestClass, numClasses);
            if (mappedFdi == 0) continue;

            teeth.Add(new DetectedTooth
            {
                FdiNumber = mappedFdi,
                Confidence = maxConf,
                X = Math.Clamp(x_norm, 0, 1),
                Y = Math.Clamp(y_norm, 0, 1),
                Width = Math.Clamp(w_norm, 0, 1),
                Height = Math.Clamp(h_norm, 0, 1)
            });
        }

        return teeth;
    }

    private List<DetectedTooth> FinalizeTeeth(List<DetectedTooth> teeth, bool preferSpatialRefine, float iouThreshold)
    {
        if (teeth.Count == 0) return teeth;
        var nms = ApplyNms(teeth, iouThreshold);
        var directNumbered = KeepBestPerFdi(nms)
            .Where(t => t.FdiNumber != 0)
            .ToList();

        var bestNumbered = directNumbered;
        bool shouldTrySpatial = preferSpatialRefine ||
                                (directNumbered.Count < CoverageTargetTeeth && nms.Count >= SpatialRescueMinCandidates);

        if (shouldTrySpatial)
        {
            var spatialInput = nms.Select(CloneTooth).ToList();
            var refined = _fdiService.RefineFdiNumbering(spatialInput) ?? spatialInput;
            var spatialNumbered = KeepBestPerFdi(refined)
                .Where(t => t.FdiNumber != 0)
                .ToList();

            if (ShouldPreferSpatial(directNumbered, spatialNumbered))
            {
                bestNumbered = spatialNumbered;
            }
        }

        return bestNumbered
            .OrderByDescending(t => t.Confidence)
            .Take(MaxAdultTeeth)
            .ToList();
    }

    private static bool ShouldPreferSpatial(List<DetectedTooth> direct, List<DetectedTooth> spatial)
    {
        if (spatial.Count == 0)
            return false;
        if (direct.Count == 0)
            return true;

        if (spatial.Count >= direct.Count + 2)
            return true;
        if (direct.Count < 20 && spatial.Count > direct.Count)
            return true;

        if (spatial.Count == direct.Count)
        {
            float directMeanConfidence = direct.Average(t => t.Confidence);
            float spatialMeanConfidence = spatial.Average(t => t.Confidence);
            return spatialMeanConfidence >= directMeanConfidence + 0.03f;
        }

        return false;
    }

    private static DetectedTooth CloneTooth(DetectedTooth source)
    {
        return new DetectedTooth
        {
            FdiNumber = source.FdiNumber,
            Confidence = source.Confidence,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height
        };
    }

    private static bool IsPlausibleToothBox(float width, float height, bool strictMode)
    {
        float minWidth = strictMode ? MinNormalizedWidth : RescueMinWidth;
        float minHeight = strictMode ? MinNormalizedHeight : RescueMinHeight;
        float minArea = strictMode ? MinToothArea : RescueMinToothArea;
        float maxArea = strictMode ? MaxToothArea : RescueMaxToothArea;
        float minRatio = strictMode ? MinToothAspectRatio : RescueMinAspectRatio;
        float maxRatio = strictMode ? MaxToothAspectRatio : RescueMaxAspectRatio;

        if (width < minWidth || height < minHeight)
            return false;

        var area = width * height;
        if (area < minArea || area > maxArea)
            return false;

        var ratio = width / Math.Max(height, 0.0001f);
        return ratio >= minRatio && ratio <= maxRatio;
    }

    private static List<DetectedTooth> KeepBestPerFdi(List<DetectedTooth> detections)
    {
        return detections
            .GroupBy(t => t.FdiNumber)
            .Select(g => g.OrderByDescending(t => t.Confidence).First())
            .ToList();
    }

    private List<DetectedTooth> ApplyCoverageSupplementIfNeeded(
        Tensor<float> output,
        int inputSize,
        float padX,
        float padY,
        int numPredictions,
        int numClasses,
        List<DetectedTooth> current,
        bool applySigmoidScores)
    {
        if (_config.FdiMapping.ClassMap.Length < MinDirectClassMapLengthForTrustedFdi)
            return current;

        if (current.Count >= MaxAdultTeeth)
            return current;

        bool isSparseCoverage = current.Count < CoverageTargetTeeth;
        float classConfidenceFloor = isSparseCoverage
            ? CoverageClassConfidenceFloorSparse
            : CoverageClassConfidenceFloorDense;

        var bestByFdi = current
            .Where(t => t.FdiNumber > 0)
            .GroupBy(t => t.FdiNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Confidence).First());

        int classLimit = numClasses;
        for (int c = 0; c < classLimit; c++)
        {
            int fdi = MapClassIndexToFdi(c, numClasses);
            if (fdi == 0 || bestByFdi.ContainsKey(fdi))
                continue;

            float bestConfidence = 0f;
            DetectedTooth? bestCandidate = null;

            for (int i = 0; i < numPredictions; i++)
            {
                float conf = NormalizeClassScore(output[0, c + 4, i], applySigmoidScores);
                if (conf < classConfidenceFloor || conf <= bestConfidence)
                    continue;

                if (!TryCreateToothCandidate(output, inputSize, padX, padY, i, fdi, conf, strictMode: false, out var candidate))
                    continue;

                bestConfidence = conf;
                bestCandidate = candidate;
            }

            if (bestCandidate != null)
            {
                bestByFdi[fdi] = bestCandidate;
            }
        }

        return bestByFdi.Values
            .OrderByDescending(t => t.Confidence)
            .Take(MaxAdultTeeth)
            .ToList();
    }

    private bool TryCreateToothCandidate(
        Tensor<float> output,
        int inputSize,
        float padX,
        float padY,
        int predictionIndex,
        int fdiNumber,
        float confidence,
        bool strictMode,
        out DetectedTooth candidate)
    {
        candidate = null!;

        float w = output[0, 2, predictionIndex];
        float h = output[0, 3, predictionIndex];
        float xCanvas = output[0, 0, predictionIndex] - w / 2;
        float yCanvas = output[0, 1, predictionIndex] - h / 2;
        float validWidth = inputSize - 2 * padX;
        float validHeight = inputSize - 2 * padY;
        float xNorm = (xCanvas - padX) / validWidth;
        float yNorm = (yCanvas - padY) / validHeight;
        float wNorm = w / validWidth;
        float hNorm = h / validHeight;

        if (xNorm < -0.1f || yNorm < -0.1f || xNorm > 1.1f || yNorm > 1.1f)
            return false;

        if (!IsPlausibleToothBox(wNorm, hNorm, strictMode))
            return false;

        candidate = new DetectedTooth
        {
            FdiNumber = fdiNumber,
            Confidence = confidence,
            X = Math.Clamp(xNorm, 0, 1),
            Y = Math.Clamp(yNorm, 0, 1),
            Width = Math.Clamp(wNorm, 0, 1),
            Height = Math.Clamp(hNorm, 0, 1)
        };

        return true;
    }

    private static bool ShouldApplySigmoidScores(Tensor<float> output, int numPredictions, int numClasses)
    {
        // Some exported YOLO heads emit logits instead of probabilities.
        // If we observe values outside [0..1], normalize with sigmoid.
        int sampledPredictions = Math.Min(numPredictions, 160);
        int step = Math.Max(1, numPredictions / sampledPredictions);

        for (int i = 0; i < numPredictions; i += step)
        {
            for (int c = 0; c < numClasses; c++)
            {
                float raw = output[0, c + 4, i];
                if (raw < 0f || raw > 1f)
                    return true;
            }
        }

        return false;
    }

    private static float NormalizeClassScore(float rawScore, bool applySigmoid)
    {
        if (!applySigmoid)
            return rawScore;

        // Numerically stable sigmoid.
        if (rawScore >= 0f)
        {
            float z = MathF.Exp(-rawScore);
            return 1f / (1f + z);
        }

        float ez = MathF.Exp(rawScore);
        return ez / (1f + ez);
    }

    private int MapClassIndexToFdi(int classIndex, int numClasses)
    {
        if (classIndex >= 0 && classIndex < _config.FdiMapping.ClassMap.Length)
        {
            return _config.FdiMapping.ClassMap[classIndex];
        }

        // Compatibility fallback for 54-class dental models:
        // 0..31 permanent, 32..51 deciduous, 52 paramolar, 53 unidentified.
        // For adult panoramic workflow, remap deciduous classes to nearest permanent counterparts.
        if (_config.FdiMapping.ClassMap.Length <= 32 && numClasses >= 54)
        {
            return classIndex switch
            {
                >= 0 and <= 7 => 11 + classIndex,
                >= 8 and <= 15 => 21 + (classIndex - 8),
                >= 16 and <= 23 => 31 + (classIndex - 16),
                >= 24 and <= 31 => 41 + (classIndex - 24),
                >= 32 and <= 36 => 11 + (classIndex - 32), // 51..55 -> 11..15
                >= 37 and <= 41 => 21 + (classIndex - 37), // 61..65 -> 21..25
                >= 42 and <= 46 => 31 + (classIndex - 42), // 71..75 -> 31..35
                >= 47 and <= 51 => 41 + (classIndex - 47), // 81..85 -> 41..45
                _ => 0
            };
        }

        return 0;
    }

    private static bool IsPlausiblePathologyBox(string className, float width, float height)
    {
        if (width < MinPathologyWidth || height < MinPathologyHeight)
            return false;

        float area = width * height;
        if (area <= 0f)
            return false;

        float maxArea = className.Equals("Missing teeth", StringComparison.OrdinalIgnoreCase)
            ? MaxMissingToothArea
            : MaxPathologyArea;

        if (area > maxArea)
            return false;

        float ratio = width / Math.Max(height, 0.0001f);
        return ratio >= 0.05f && ratio <= 8.5f;
    }

    private static bool IsPointInside(float x, float y, float width, float height, float px, float py)
    {
        return px >= x && px <= x + width && py >= y && py <= y + height;
    }

    private static bool AllowProximityFallback(string className)
    {
        return className.Equals("Missing teeth", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("Periapical lesion", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("Root Piece", StringComparison.OrdinalIgnoreCase);
    }

    private static float GetPathologyProximityThreshold(string className, float defaultThreshold)
    {
        float tightened = className switch
        {
            "Missing teeth" => 0.08f,
            "Periapical lesion" => 0.06f,
            "Root Piece" => 0.06f,
            _ => defaultThreshold
        };

        return Math.Min(defaultThreshold, tightened);
    }

    private static int GetPathologyPerClassCap(string className)
    {
        return className switch
        {
            "Caries" => 14,
            "Missing teeth" => 12,
            "Periapical lesion" => 10,
            _ => 8
        };
    }

    private static bool IsReliablePathologyClassDecision(float maxConfidence, float secondConfidence, float threshold)
    {
        float margin = maxConfidence - secondConfidence;
        if (margin >= PathologyClassMarginMin)
            return true;

        // If class scores are very close, only accept predictions with strong absolute confidence.
        return maxConfidence >= Math.Max(PathologyMarginHighConfidenceBypass, threshold + 0.18f);
    }

}
