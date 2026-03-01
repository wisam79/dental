using System.Text.Json;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.Enums;


namespace DentalID.Application.Services;

/// <summary>
/// Service for performing forensic dental analysis
/// </summary>
public class ForensicAnalysisService : IForensicAnalysisService
{
    private readonly IAiPipelineService _aiPipeline;
    private readonly ILoggerService _logger;
    private readonly IIntegrityService _integrityService;
    private readonly IDentalImageRepository _imageRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly AiConfiguration _config; // For thresholds
    private readonly AiSettings _aiSettings;
    private readonly IFileService _fileService;

    /// <summary>
    /// Constructor for ForensicAnalysisService
    /// </summary>
    /// <param name="aiPipeline">AI pipeline service for analysis</param>
    /// <param name="logger">Logger service for audit and error logging</param>
    /// <param name="integrityService">Integrity service for file hashing</param>
    /// <param name="imageRepo">Repository for dental images</param>
    /// <param name="subjectRepo">Repository for subjects</param>
    /// <param name="config">AI configuration with thresholds</param>
    /// <param name="aiSettings">AI settings including security keys</param>
    /// <param name="fileService">File service for file operations</param>
    public ForensicAnalysisService(
        IAiPipelineService aiPipeline,
        ILoggerService logger,
        IIntegrityService integrityService,
        IDentalImageRepository imageRepo,
        ISubjectRepository subjectRepo,
        AiConfiguration config,
        AiSettings aiSettings,
        IFileService fileService)
    {
        _aiPipeline = aiPipeline;
        _logger = logger;
        _integrityService = integrityService;
        _imageRepo = imageRepo;
        _subjectRepo = subjectRepo;
        _config = config;
        _aiSettings = aiSettings;
        _fileService = fileService;
    }

    /// <inheritdoc/>
    public async Task<AnalysisResult> AnalyzeImageAsync(string imagePath, double sensitivity = 0.5)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be null or empty", nameof(imagePath));
        
        if (!_fileService.Exists(imagePath))
            throw new FileNotFoundException("Image file not found", imagePath);
        
        if (sensitivity < 0 || sensitivity > 1)
            throw new ArgumentOutOfRangeException(nameof(sensitivity), "Sensitivity must be between 0 and 1");

        try
        {
            _logger.LogAudit("JOB_START", "User", $"Requesting analysis for {Path.GetFileName(imagePath)} with sensitivity {sensitivity:F2}");
            
            await using var stream = _fileService.OpenRead(imagePath);
            var result = await _aiPipeline.AnalyzeImageAsync(stream, Path.GetFileName(imagePath));
            
            if (result.IsSuccess)
            {
                // Clone before filtering to avoid mutating cached/shared analysis objects.
                var filteredResult = CloneAnalysisResult(result);
                UpdateForensicFilter(filteredResult, sensitivity);
                return filteredResult;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis Job Failed");
            return new AnalysisResult { Error = "System Error during analysis." };
        }
    }

    /// <inheritdoc/>
    public void UpdateForensicFilter(AnalysisResult result, double sensitivity)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
            
        if (sensitivity < 0 || sensitivity > 1)
            throw new ArgumentOutOfRangeException(nameof(sensitivity), "Sensitivity must be between 0 and 1");
            
        // 1. Calculate Threshold
        // Sensitivity 0.0 -> Threshold (Base) (Very Strict)
        // Sensitivity 1.0 -> Threshold (Base - Slope) (Very Loose)
        double baseThreshold = _config.Thresholds.ForensicBaseThreshold - (sensitivity * _config.Thresholds.SensitivitySlope);
        double effectiveTeethThreshold = Math.Min(baseThreshold, Math.Max(0.20, _config.Thresholds.TeethThreshold + 0.02));

        // Bug #18 fix: Guard against null RawTeeth to avoid silently clearing result.Teeth
        var rawTeeth = result.RawTeeth ?? result.Teeth ?? new List<DetectedTooth>();
        var filteredTeeth = rawTeeth
            .Where(t => t.Confidence >= effectiveTeethThreshold)
            .Where(IsValidNormalizedBox)
            .ToList();
        // Re-apply geometric suppression after sensitivity filter to avoid surfacing pre-NMS TTA duplicates.
        filteredTeeth = ApplyToothNms(filteredTeeth, _aiSettings.IouThreshold);

        List<DetectedTooth> BuildFinalTeethSet(IEnumerable<DetectedTooth> source) => source
            .Where(t => t.FdiNumber > 0)
            .GroupBy(t => t.FdiNumber)
            .Select(g => g.OrderByDescending(t => t.Confidence).First())
            .OrderBy(t => t.FdiNumber)
            .Take(32)
            .ToList();
        
        var finalTeeth = BuildFinalTeethSet(filteredTeeth);

        // Rescue pass: avoid under-detection when forensic filtering is too aggressive.
        if (finalTeeth.Count < 28 && rawTeeth.Count > finalTeeth.Count)
        {
            double rescueThreshold = Math.Max(0.16, Math.Min(effectiveTeethThreshold, _config.Thresholds.TeethThreshold * 0.70));
            var rescueTeeth = rawTeeth
                .Where(t => t.Confidence >= rescueThreshold)
                .Where(IsValidNormalizedBox)
                .ToList();

            var mergedTeeth = filteredTeeth
                .Concat(rescueTeeth)
                .OrderByDescending(t => t.Confidence)
                .ToList();
            mergedTeeth = ApplyToothNms(mergedTeeth, Math.Min(_aiSettings.IouThreshold, 0.42f));

            finalTeeth = BuildFinalTeethSet(mergedTeeth);
        }

        // Progressive relaxation: step down confidence gate until we recover enough unique FDI teeth.
        if (finalTeeth.Count < 28)
        {
            for (double threshold = Math.Min(effectiveTeethThreshold, 0.34); threshold >= 0.12 && finalTeeth.Count < 28; threshold -= 0.04)
            {
                var relaxedCandidates = rawTeeth
                    .Where(t => t.Confidence >= threshold)
                    .Where(IsValidNormalizedBox)
                    .ToList();
                relaxedCandidates = ApplyToothNms(relaxedCandidates, Math.Min(_aiSettings.IouThreshold, 0.44f));

                var relaxedFinal = BuildFinalTeethSet(relaxedCandidates);
                if (relaxedFinal.Count > finalTeeth.Count)
                {
                    finalTeeth = relaxedFinal;
                }
            }
        }

        // Coverage supplement: for sparse outputs, merge best remaining FDI detections from raw candidates.
        if (finalTeeth.Count < 28)
        {
            var supplementation = rawTeeth
                .Where(IsValidNormalizedBox)
                .Where(t => t.FdiNumber > 0 && t.Confidence >= 0.12f)
                .GroupBy(t => t.FdiNumber)
                .Select(g => g.OrderByDescending(t => t.Confidence).First())
                .ToList();

            finalTeeth = finalTeeth
                .Concat(supplementation)
                .GroupBy(t => t.FdiNumber)
                .Select(g => g.OrderByDescending(t => t.Confidence).First())
                .OrderBy(t => t.FdiNumber)
                .Take(32)
                .ToList();
        }

        // Hard safety-net: if we still have a suspiciously low count, keep best per FDI from valid raw detections.
        if (finalTeeth.Count < 16)
        {
            var rawFallback = BuildFinalTeethSet(rawTeeth.Where(IsValidNormalizedBox));
            if (rawFallback.Count > finalTeeth.Count)
            {
                finalTeeth = rawFallback;
            }
        }

        result.Teeth = finalTeeth;

        // Prefer the richer pathology source to avoid stale lists after augmentation steps (for example TTA).
        var rawPathologiesCandidate = result.RawPathologies ?? new List<DetectedPathology>();
        var currentPathologiesCandidate = result.Pathologies ?? new List<DetectedPathology>();
        var rawPathologies = currentPathologiesCandidate.Count > rawPathologiesCandidate.Count
            ? currentPathologiesCandidate
            : rawPathologiesCandidate;

        // 3. Filter Pathologies from raw list with class bias
        var filteredPathologies = rawPathologies
            .Where(p => 
            {
                double classBias = GetClassBias(p.ClassName);
                double threshold = Math.Max(baseThreshold + classBias, GetPathologyConfidenceFloor(p.ClassName));
                return p.Confidence >= threshold;
            })
            .Where(IsValidNormalizedBox)
            .ToList();
        filteredPathologies = ApplyPathologyNms(filteredPathologies, Math.Min(_aiSettings.IouThreshold, 0.35f));
        var detectedToothNumbers = result.Teeth.Select(t => t.FdiNumber).ToHashSet();
        var toothByFdi = result.Teeth
            .Where(t => t.FdiNumber > 0)
            .GroupBy(t => t.FdiNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Confidence).First());
        filteredPathologies = filteredPathologies
            .Where(p => p.ToothNumber.HasValue && detectedToothNumbers.Contains(p.ToothNumber.Value))
            .Where(p => IsPathologySpatiallyConsistent(p, toothByFdi))
            .GroupBy(p => new { p.ToothNumber, p.ClassName })
            .Select(g => g.OrderByDescending(p => p.Confidence).First())
            .GroupBy(p => p.ToothNumber!.Value)
            .SelectMany(g => ApplyPerToothCombinationCaps(g)
                .OrderByDescending(p => p.Confidence)
                .Take(GetPathologyPerToothCap()))
            .GroupBy(p => p.ClassName)
            .SelectMany(g => g
                .OrderByDescending(p => p.Confidence)
                .Take(GetPathologyPerClassCap(g.Key)))
            .ToList();
        int dynamicPathologyCap = GetDynamicPathologyCap(result.Teeth.Count);
        result.Pathologies = filteredPathologies
            .OrderByDescending(p => p.Confidence)
            .Take(dynamicPathologyCap)
            .ToList();

        // Rebuild operator-facing flags from filtered detections to avoid noisy raw-model alerts.
        var preservedForensicAlerts = result.Flags
            .Where(f => f.StartsWith("Forensic Alert", StringComparison.OrdinalIgnoreCase) ||
                        f.StartsWith("Forensic Note", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        result.Flags.Clear();
        result.Flags.AddRange(preservedForensicAlerts);
        new ForensicRulesEngine().ApplyRules(result);
    }

    /// <inheritdoc/>
    public async Task<DentalImage> SaveEvidenceAsync(string sourcePath, AnalysisResult result, int subjectId)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be null or empty", nameof(sourcePath));
        
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        
        if (subjectId <= 0)
            throw new ArgumentException("Subject ID must be positive", nameof(subjectId));
        
        if (!_fileService.Exists(sourcePath))
            throw new FileNotFoundException("Source file not found", sourcePath);

        var subject = await _subjectRepo.GetByIdAsync(subjectId);
        if (subject == null)
            throw new InvalidOperationException($"Subject {subjectId} not found.");

        _logger.LogInformation($"Starting evidence preservation for Subject {subjectId}");

        // 1. Atomic File Write (The Forensic Way)
        string imagesDir = Path.Combine(AppContext.BaseDirectory, "data", "images");
        Directory.CreateDirectory(imagesDir);
        
        string ext = Path.GetExtension(sourcePath);
        string uniqueName = $"{Guid.NewGuid()}{ext}";
        string tempPath = Path.Combine(imagesDir, $"{uniqueName}.tmp");
        string finalPath = Path.Combine(imagesDir, uniqueName);

        string fileHash;

        try
        {
            // Copy to .tmp first
            _fileService.Copy(sourcePath, tempPath, overwrite: true);

            // Compute Hash of the COPIED file (ensure what we have on disk is what we hash)
            fileHash = await _integrityService.ComputeFileHashAsync(tempPath);

            // Rename to final (Atomic operation on NTFS/ext4)
            _fileService.Move(tempPath, finalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File preservation failed");
            // Cleanup temp
            if (_fileService.Exists(tempPath)) _fileService.Delete(tempPath);
            throw new IOException("Failed to securely save evidence file.", ex);
        }

        // 2. Seal Data with Hash
        var resultJson = JsonSerializer.Serialize(result);
        if (string.IsNullOrEmpty(resultJson))
        {
            throw new InvalidOperationException("Failed to serialize analysis result");
        }
        
        // Bug #19 fix: Add pipe separators to prevent hash collision between different field combinations
        string digitalSealSource = $"{fileHash}|{resultJson}|{subjectId}";
        string digitalSeal = ComputeSeal(digitalSealSource);

        // 3. Database Transaction (Bug #16 fix: use try/catch for consistency)
        var featureVector = result.FeatureVector;
        byte[]? featureVectorBytes = null;
        if (featureVector != null && featureVector.Length > 0)
        {
            featureVectorBytes = new byte[featureVector.Length * sizeof(float)];
            Buffer.BlockCopy(featureVector, 0, featureVectorBytes, 0, featureVectorBytes.Length);
        }

        var imageEntity = new DentalImage
        {
            SubjectId = subjectId,
            ImagePath = finalPath,
            FileHash = fileHash,
            ImageType = ImageType.Panoramic,
            IsProcessed = true,
            UploadedAt = DateTime.UtcNow,
            CaptureDate = DateTime.UtcNow,
            AnalysisResults = resultJson,
            // Bug #2 fix: store the digital seal
            DigitalSeal = digitalSeal,
            // Bug #3 fix: store fingerprint code and uniqueness
            FingerprintCode = result.Fingerprint?.Code,
            UniquenessScore = result.Fingerprint?.UniquenessScore ?? 0,
        };

        bool imagePersisted = false;
        try
        {
            await _imageRepo.AddAsync(imageEntity);
            imagePersisted = true;

            // 4. Update Subject Vector (if present)
            if (featureVectorBytes != null)
            {
                subject.FeatureVector = featureVectorBytes;
                await _subjectRepo.UpdateAsync(subject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database operations failed during evidence save");
            // Compensate persisted image if subject update failed.
            if (imagePersisted && imageEntity.Id > 0)
            {
                try
                {
                    await _imageRepo.DeleteAsync(imageEntity.Id);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, $"Failed to compensate image record {imageEntity.Id} after DB failure.");
                }
            }
            else if (_fileService.Exists(finalPath))
            {
                _fileService.Delete(finalPath);
            }

            throw new IOException("Failed to persist evidence to database.", ex);
        }

        _logger.LogAudit("EVIDENCE_SAVED", "User", $"Saved {uniqueName} to Subject {subjectId}", digitalSeal);
        
        return imageEntity;
    }

    /// <summary>
    /// Gets bias value for pathology class
    /// </summary>
    /// <param name="className">Pathology class name</param>
    /// <returns>Bias value to add to threshold</returns>
    private double GetClassBias(string className)
    {
        if (_config.Thresholds.PathologyBias.TryGetValue(className, out double bias))
            return bias;
        // Bug #22 fix: Log a warning when an unknown class name is encountered (typos are now visible)
        _logger.LogWarning($"Unknown pathology class '{className}' not found in PathologyBias config — using default bias 0.0");
        return 0.0;
    }

    private static double GetPathologyConfidenceFloor(string className) => className switch
    {
        "Caries" => 0.52,
        "Periapical lesion" => 0.50,
        "Missing teeth" => 0.48,
        _ => 0.42
    };

    private static int GetPathologyPerToothCap() => 3;

    private static int GetPathologyPerClassCap(string className) => className switch
    {
        "Caries" => 14,
        "Missing teeth" => 12,
        "Periapical lesion" => 10,
        _ => 8
    };

    private static int GetDynamicPathologyCap(int detectedTeethCount)
    {
        // Keeps pathology count proportional to detected teeth to prevent noisy floods.
        return Math.Clamp((detectedTeethCount * 2) + 6, 12, 40);
    }

    private static bool IsPathologySpatiallyConsistent(
        DetectedPathology pathology,
        IReadOnlyDictionary<int, DetectedTooth> toothByFdi)
    {
        if (!pathology.ToothNumber.HasValue)
            return false;
        if (!toothByFdi.TryGetValue(pathology.ToothNumber.Value, out var tooth))
            return false;

        float left = Math.Max(pathology.X, tooth.X);
        float top = Math.Max(pathology.Y, tooth.Y);
        float right = Math.Min(pathology.X + pathology.Width, tooth.X + tooth.Width);
        float bottom = Math.Min(pathology.Y + pathology.Height, tooth.Y + tooth.Height);
        float overlapWidth = Math.Max(0f, right - left);
        float overlapHeight = Math.Max(0f, bottom - top);
        float overlapArea = overlapWidth * overlapHeight;
        float pathologyArea = Math.Max(pathology.Width * pathology.Height, 1e-6f);
        float overlapRatio = overlapArea / pathologyArea;

        float pCenterX = pathology.X + pathology.Width / 2f;
        float pCenterY = pathology.Y + pathology.Height / 2f;
        float tCenterX = tooth.X + tooth.Width / 2f;
        float tCenterY = tooth.Y + tooth.Height / 2f;
        float centerDistance = (float)Math.Sqrt(
            Math.Pow(pCenterX - tCenterX, 2) +
            Math.Pow(pCenterY - tCenterY, 2));

        var (minOverlap, maxDistance) = GetSpatialRules(pathology.ClassName);
        return overlapRatio >= minOverlap || centerDistance <= maxDistance;
    }

    private static (float MinOverlap, float MaxCenterDistance) GetSpatialRules(string className)
    {
        return className switch
        {
            "Implant" => (0.12f, 0.04f),
            "Crown" => (0.10f, 0.045f),
            "Filling" => (0.10f, 0.045f),
            "Root canal obturation" => (0.08f, 0.05f),
            "Missing teeth" => (0.00f, 0.06f),
            "Periapical lesion" => (0.02f, 0.05f),
            "Root Piece" => (0.03f, 0.05f),
            _ => (0.08f, 0.06f)
        };
    }

    private static IEnumerable<DetectedPathology> ApplyPerToothCombinationCaps(
        IGrouping<int, DetectedPathology> group)
    {
        var items = group
            .OrderByDescending(p => p.Confidence)
            .ToList();

        var restorative = items
            .Where(p => IsRestorativeClass(p.ClassName))
            .Take(3)
            .ToList();

        var nonRestorative = items
            .Where(p => !IsRestorativeClass(p.ClassName))
            .ToList();

        return restorative.Concat(nonRestorative);
    }

    private static bool IsRestorativeClass(string className)
    {
        return className == "Implant" ||
               className == "Crown" ||
               className == "Filling" ||
               className == "Root canal obturation";
    }

    /// <summary>
    /// Computes digital seal for evidence integrity verification.
    /// Uses HMAC-SHA256 with a cryptographic key for proper authentication.
    /// </summary>
    /// <param name="rawData">Raw data to seal</param>
    /// <returns>HMAC-SHA256 hash as hex string</returns>
    private string ComputeSeal(string rawData)
    {
        // Use a proper cryptographic key from configuration or generate one
        var keySource = _aiSettings.SealingKey;
        if (IsInsecureSealingKey(keySource))
        {
            // STRICT SECURITY ENFORCEMENT: No Fallback
            var msg = "CRITICAL SECURITY FAILURE: No valid 'SealingKey' configured. " +
                      "The application cannot guarantee forensic integrity and will not proceed. " +
                      "Please configure a secure key in appsettings.json or environment variables.";

            _logger.LogError(new System.Security.SecurityException(msg), "Security Configuration missing");
            throw new System.Security.SecurityException(msg);
        }

        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(keySource));
        
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawData);
        var hash = hmac.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static bool IsInsecureSealingKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return true;

        return key.Contains("CHANGE-THIS", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("DO-NOT-USE", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("ChangeMeToASecureRandomKey", StringComparison.OrdinalIgnoreCase) ||
               key.Length < 16;
    }
    
    /// <summary>
    /// Verifies the digital seal of stored evidence.
    /// </summary>
    /// <param name="storedSeal">The stored seal to verify</param>
    /// <param name="fileHash">Original file hash</param>
    /// <param name="resultJson">Original analysis result JSON</param>
    /// <param name="subjectId">Subject ID</param>
    /// <returns>True if seal is valid</returns>
    public bool VerifySeal(string storedSeal, string fileHash, string resultJson, int subjectId)
    {
        // Bug #19 fix: Consistent separator usage matching ComputeSeal
        var rawData = $"{fileHash}|{resultJson}|{subjectId}";
        var computedSeal = ComputeSeal(rawData);
        // Bug #20 fix: Hex strings are always lowercase; use Ordinal instead of OrdinalIgnoreCase
        return string.Equals(storedSeal, computedSeal, StringComparison.Ordinal);
    }

    private static AnalysisResult CloneAnalysisResult(AnalysisResult source)
    {
        var sourceTeeth = source.Teeth ?? new List<DetectedTooth>();
        var sourcePathologies = source.Pathologies ?? new List<DetectedPathology>();
        var sourceRawTeeth = source.RawTeeth ?? new List<DetectedTooth>();
        var sourceRawPathologies = source.RawPathologies ?? new List<DetectedPathology>();

        return new AnalysisResult
        {
            Teeth = sourceTeeth.Select(t => new DetectedTooth
            {
                FdiNumber = t.FdiNumber,
                Confidence = t.Confidence,
                X = t.X,
                Y = t.Y,
                Width = t.Width,
                Height = t.Height
            }).ToList(),
            Pathologies = sourcePathologies.Select(p => new DetectedPathology
            {
                ClassName = p.ClassName,
                Confidence = p.Confidence,
                ToothNumber = p.ToothNumber,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height
            }).ToList(),
            RawTeeth = sourceRawTeeth.Select(t => new DetectedTooth
            {
                FdiNumber = t.FdiNumber,
                Confidence = t.Confidence,
                X = t.X,
                Y = t.Y,
                Width = t.Width,
                Height = t.Height
            }).ToList(),
            RawPathologies = sourceRawPathologies.Select(p => new DetectedPathology
            {
                ClassName = p.ClassName,
                Confidence = p.Confidence,
                ToothNumber = p.ToothNumber,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height
            }).ToList(),
            EstimatedAge = source.EstimatedAge,
            EstimatedGender = source.EstimatedGender,
            FeatureVector = source.FeatureVector?.ToArray(),
            Fingerprint = source.Fingerprint == null ? null : new DentalFingerprint
            {
                Code = source.Fingerprint.Code,
                UniquenessScore = source.Fingerprint.UniquenessScore,
                ToothMap = source.Fingerprint.ToothMap?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<int, string>(),
                Features = source.Fingerprint.Features?.ToList() ?? new List<string>(),
                FeatureVector = source.Fingerprint.FeatureVector?.ToArray()
            },
            ProcessingTimeMs = source.ProcessingTimeMs,
            Error = source.Error,
            Flags = source.Flags?.ToList() ?? new List<string>(),
            SmartInsights = source.SmartInsights?.ToList() ?? new List<string>()
        };
    }

    private static bool IsValidNormalizedBox(DetectedTooth tooth)
    {
        return tooth.Width > 0 && tooth.Height > 0 &&
               tooth.Width <= 1 && tooth.Height <= 1 &&
               tooth.X >= 0 && tooth.Y >= 0 &&
               tooth.X <= 1 && tooth.Y <= 1;
    }

    private static bool IsValidNormalizedBox(DetectedPathology pathology)
    {
        return pathology.Width > 0 && pathology.Height > 0 &&
               pathology.Width <= 1 && pathology.Height <= 1 &&
               pathology.X >= 0 && pathology.Y >= 0 &&
               pathology.X <= 1 && pathology.Y <= 1;
    }

    private static List<DetectedTooth> ApplyToothNms(List<DetectedTooth> detections, float iouThreshold)
    {
        if (detections.Count <= 1)
            return detections;

        var sorted = detections.OrderByDescending(t => t.Confidence).ToList();
        var results = new List<DetectedTooth>(sorted.Count);
        var suppressed = new bool[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i])
                continue;

            var current = sorted[i];
            results.Add(current);

            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed[j])
                    continue;

                var other = sorted[j];
                var iou = ForensicHeuristicsService.CalculateIoU(
                    current.X, current.Y, current.Width, current.Height,
                    other.X, other.Y, other.Width, other.Height);

                if (iou > iouThreshold)
                    suppressed[j] = true;
            }
        }

        return results;
    }

    private static List<DetectedPathology> ApplyPathologyNms(List<DetectedPathology> detections, float iouThreshold)
    {
        if (detections.Count <= 1)
            return detections;

        var sorted = detections.OrderByDescending(p => p.Confidence).ToList();
        var results = new List<DetectedPathology>(sorted.Count);
        var suppressed = new bool[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i])
                continue;

            var current = sorted[i];
            results.Add(current);

            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed[j])
                    continue;

                var other = sorted[j];
                if (!string.Equals(current.ClassName, other.ClassName, StringComparison.Ordinal))
                    continue;

                var iou = ForensicHeuristicsService.CalculateIoU(
                    current.X, current.Y, current.Width, current.Height,
                    other.X, other.Y, other.Width, other.Height);

                if (iou > iouThreshold)
                    suppressed[j] = true;
            }
        }

        return results;
    }
}
