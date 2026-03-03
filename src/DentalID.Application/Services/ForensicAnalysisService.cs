using System.Text.Json;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.Enums;
using SkiaSharp;


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
    private readonly IForensicRulesEngine _rulesEngine;

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
    /// <param name="rulesEngine">The rules engine to apply post-processing heuristics</param>
    public ForensicAnalysisService(
        IAiPipelineService aiPipeline,
        ILoggerService logger,
        IIntegrityService integrityService,
        IDentalImageRepository imageRepo,
        ISubjectRepository subjectRepo,
        AiConfiguration config,
        AiSettings aiSettings,
        IFileService fileService,
        IForensicRulesEngine rulesEngine)
    {
        _aiPipeline = aiPipeline;
        _logger = logger;
        _integrityService = integrityService;
        _imageRepo = imageRepo;
        _subjectRepo = subjectRepo;
        _config = config;
        _aiSettings = aiSettings;
        _fileService = fileService;
        _rulesEngine = rulesEngine;
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
            
            await using var inputStream = _fileService.OpenRead(imagePath);
            using var imageBuffer = new MemoryStream();
            await inputStream.CopyToAsync(imageBuffer);
            var imageBytes = imageBuffer.ToArray();

            SKBitmap? bitmap = null;
            try
            {
                bitmap = SKBitmap.Decode(imageBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Rejected analysis input (decode failed): {Path.GetFileName(imagePath)} ({ex.Message})");
                return new AnalysisResult { Error = "Invalid image format. Please load a valid dental radiograph image." };
            }

            using (bitmap)
            {
            if (bitmap == null)
            {
                _logger.LogWarning($"Rejected analysis input (invalid image): {Path.GetFileName(imagePath)}");
                return new AnalysisResult { Error = "Invalid image format. Please load a valid dental radiograph image." };
            }

            if (!LooksLikeDentalRadiograph(bitmap, out var rejectReason))
            {
                _logger.LogAudit("JOB_REJECTED", "User", $"Rejected non-dental image input: {Path.GetFileName(imagePath)} ({rejectReason})");
                return new AnalysisResult
                {
                    Error = rejectReason,
                    Flags = new List<string> { "Forensic Alert: Unsupported evidence type. Input is not a dental radiograph." }
                };
            }

                await using var pipelineStream = new MemoryStream(imageBytes, writable: false);
                var result = await _aiPipeline.AnalyzeImageAsync(pipelineStream, Path.GetFileName(imagePath));
            
                if (result.IsSuccess)
                {
                    // Clone before filtering to avoid mutating cached/shared analysis objects.
                    var filteredResult = CloneAnalysisResult(result);
                    UpdateForensicFilter(filteredResult, sensitivity);
                    return filteredResult;
                }
            
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis Job Failed");
            return new AnalysisResult { Error = "System Error during analysis." };
        }
    }

    private static bool LooksLikeDentalRadiograph(SKBitmap bitmap, out string reason)
    {
        reason = string.Empty;

        int step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 220);
        long samples = 0;
        double satSum = 0;
        double channelDiffSum = 0;
        long grayLikePixels = 0;

        for (int y = 0; y < bitmap.Height; y += step)
        {
            for (int x = 0; x < bitmap.Width; x += step)
            {
                var px = bitmap.GetPixel(x, y);
                byte r = px.Red;
                byte g = px.Green;
                byte b = px.Blue;

                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));
                double sat = (max - min) / 255.0;
                double channelDiff = (Math.Abs(r - g) + Math.Abs(g - b) + Math.Abs(r - b)) / (3.0 * 255.0);

                satSum += sat;
                channelDiffSum += channelDiff;
                if (sat <= 0.10 && channelDiff <= 0.08)
                    grayLikePixels++;

                samples++;
            }
        }

        if (samples == 0)
        {
            reason = "Unable to sample image pixels for radiograph validation.";
            return false;
        }

        double meanSat = satSum / samples;
        double meanChannelDiff = channelDiffSum / samples;
        double grayRatio = grayLikePixels / (double)samples;

        // Panoramic X-rays are mostly grayscale; strongly colored scenes are out-of-domain.
        if (meanSat > 0.14 || meanChannelDiff > 0.12 || grayRatio < 0.62)
        {
            reason = "Input image is not a dental panoramic radiograph (X-ray).";
            return false;
        }

        return true;
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
        bool normalizeDeciduousAsAdult = ShouldNormalizeDeciduousToPermanent(rawTeeth);
        if (normalizeDeciduousAsAdult)
        {
            rawTeeth = CanonicalizeToothFdi(rawTeeth, normalizeDeciduousAsAdult);
            result.RawTeeth = rawTeeth;
        }
        var rawPathologiesCandidate = result.RawPathologies ?? new List<DetectedPathology>();
        var currentPathologiesCandidate = result.Pathologies ?? new List<DetectedPathology>();
        var rawPathologies = currentPathologiesCandidate.Count > rawPathologiesCandidate.Count
            ? currentPathologiesCandidate
            : rawPathologiesCandidate;
        rawPathologies = CanonicalizePathologyToothReferences(rawPathologies, normalizeDeciduousAsAdult);

        var filteredTeeth = rawTeeth
            .Where(t => t.Confidence >= effectiveTeethThreshold)
            .Where(IsValidNormalizedBox)
            .ToList();
        // Re-apply geometric suppression after sensitivity filter to avoid surfacing pre-NMS TTA duplicates.
        filteredTeeth = ApplyToothNms(filteredTeeth, _aiSettings.IouThreshold);

        List<DetectedTooth> BuildFinalTeethSet(IEnumerable<DetectedTooth> source) => source
            .Where(t => IsDisplayableFdi(t.FdiNumber))
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
            for (double threshold = Math.Min(effectiveTeethThreshold, 0.34); threshold >= 0.25 && finalTeeth.Count < 28; threshold -= 0.04)
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
                .Where(t => IsDisplayableFdi(t.FdiNumber) && t.Confidence >= 0.25f)
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

        // Near-complete rescue: when we already have 30-31 teeth, allow low-confidence
        // candidates to recover the final missing slots (if present in raw detections).
        if (finalTeeth.Count >= 30 && finalTeeth.Count < 32)
        {
            var existing = finalTeeth.Select(t => t.FdiNumber).ToHashSet();
            var allPermanentFdi = new HashSet<int>(
                Enumerable.Range(11, 8)
                    .Concat(Enumerable.Range(21, 8))
                    .Concat(Enumerable.Range(31, 8))
                    .Concat(Enumerable.Range(41, 8)));
            var missingPermanent = allPermanentFdi.Except(existing).OrderBy(x => x).ToList();

            var nearCompleteSupplement = rawTeeth
                .Where(IsValidNormalizedBox)
                .Where(t =>
                    IsPermanentFdi(t.FdiNumber) &&
                    !existing.Contains(t.FdiNumber) &&
                    // Targeted recovery: for exactly one missing permanent tooth (e.g., 47),
                    // allow a lower confidence floor to avoid losing near-threshold detections.
                    (t.Confidence >= 0.16f ||
                     (missingPermanent.Count == 1 &&
                      t.FdiNumber == missingPermanent[0] &&
                      t.Confidence >= 0.08f)))
                .GroupBy(t => t.FdiNumber)
                .Select(g => g.OrderByDescending(t => t.Confidence).First())
                .OrderByDescending(t => t.Confidence)
                .Take(32 - finalTeeth.Count)
                .ToList();

            if (nearCompleteSupplement.Count > 0)
            {
                finalTeeth = finalTeeth
                    .Concat(nearCompleteSupplement)
                    .GroupBy(t => t.FdiNumber)
                    .Select(g => g.OrderByDescending(t => t.Confidence).First())
                    .OrderBy(t => t.FdiNumber)
                    .Take(32)
                    .ToList();
            }
        }

        // Last-mile recovery: when exactly one permanent tooth is missing (e.g., 47),
        // infer it from neighboring geometry in the same quadrant.
        if (finalTeeth.Count == 31 &&
            TryRecoverSingleMissingToothByNeighborGeometry(finalTeeth, rawTeeth, rawPathologies, out var recoveredMissingFdi, out var recoveredTooth))
        {
            finalTeeth = finalTeeth
                .Concat(new[] { recoveredTooth })
                .GroupBy(t => t.FdiNumber)
                .Select(g => g.OrderByDescending(t => t.Confidence).First())
                .OrderBy(t => t.FdiNumber)
                .Take(32)
                .ToList();

            result.Flags.Add($"Forensic Note: Recovered missing tooth {recoveredMissingFdi} from adjacent geometry.");
        }
        else if (finalTeeth.Count == 31 &&
                 TryGetSingleMissingPermanentFdi(finalTeeth, out var unresolvedMissingFdi))
        {
            result.Flags.Add($"Forensic Note: Tooth {unresolvedMissingFdi} remains unresolved (no reliable raw candidate).");
        }

        // Hard safety-net: if we still have a suspiciously low count, keep best per FDI from valid raw detections.
        if (finalTeeth.Count < 16)
        {
            var rawFallback = BuildFinalTeethSet(rawTeeth.Where(IsValidNormalizedBox));
            if (rawFallback.Count > finalTeeth.Count)
            {
                finalTeeth = rawFallback;
            }
            result.Flags.Add("Forensic Alert: Low Quality Radiograph/Evidence. Insufficient teeth detected.");
        }

        result.Teeth = finalTeeth;

        // Prefer the richer pathology source to avoid stale lists after augmentation steps (for example TTA).
        // rawPathologies already normalized above and reused here.

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
        _rulesEngine.ApplyRules(result);
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
            FeatureVectorBlob = featureVectorBytes
        };

        bool imagePersisted = false;
        try
        {
            await _imageRepo.AddAsync(imageEntity).ConfigureAwait(false);
            imagePersisted = true;

            // 4. Update Subject Vector (if present)
            if (featureVectorBytes != null)
            {
                subject.FeatureVector = featureVectorBytes;
                await _subjectRepo.UpdateAsync(subject).ConfigureAwait(false);
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
                    await _imageRepo.DeleteAsync(imageEntity.Id).ConfigureAwait(false);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, $"Failed to compensate image record {imageEntity.Id} after DB failure.");
                }
            }
            
            if (_fileService.Exists(finalPath))
            {
                try { _fileService.Delete(finalPath); } catch (Exception cleanupEx) { _logger.LogWarning($"Cleanup of temporary evidence failed. {cleanupEx.Message}"); }
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

    private static List<DetectedTooth> CanonicalizeToothFdi(IEnumerable<DetectedTooth> source, bool normalizeDeciduousToPermanent)
    {
        return source
            .Where(t => t != null)
            .Select(t =>
            {
                int canonicalFdi = normalizeDeciduousToPermanent
                    ? MapDeciduousToPermanentCounterpart(t.FdiNumber)
                    : t.FdiNumber;

                if (!IsDisplayableFdi(canonicalFdi))
                    canonicalFdi = 0;

                return new DetectedTooth
                {
                    FdiNumber = canonicalFdi,
                    Confidence = t.Confidence,
                    X = t.X,
                    Y = t.Y,
                    Width = t.Width,
                    Height = t.Height,
                    Outline = t.Outline?.Select(p => (X: p.X, Y: p.Y)).ToList()
                };
            })
            .ToList();
    }

    private static List<DetectedPathology> CanonicalizePathologyToothReferences(
        IEnumerable<DetectedPathology> source,
        bool normalizeDeciduousToPermanent)
    {
        return source
            .Where(p => p != null)
            .Select(p =>
            {
                int? canonicalTooth = p.ToothNumber;
                if (normalizeDeciduousToPermanent && canonicalTooth.HasValue)
                {
                    canonicalTooth = MapDeciduousToPermanentCounterpart(canonicalTooth.Value);
                    if (!IsDisplayableFdi(canonicalTooth.Value))
                        canonicalTooth = null;
                }

                return new DetectedPathology
                {
                    ClassName = p.ClassName,
                    Confidence = p.Confidence,
                    ToothNumber = canonicalTooth,
                    X = p.X,
                    Y = p.Y,
                    Width = p.Width,
                    Height = p.Height,
                    Outline = p.Outline?.Select(o => (X: o.X, Y: o.Y)).ToList()
                };
            })
            .ToList();
    }

    private static bool ShouldNormalizeDeciduousToPermanent(IReadOnlyCollection<DetectedTooth> teeth)
    {
        if (teeth.Count == 0)
            return false;

        int permanentCount = teeth.Count(t => IsPermanentFdi(t.FdiNumber));
        int deciduousCount = teeth.Count(t => IsDeciduousFdi(t.FdiNumber));
        if (deciduousCount == 0)
            return false;

        // Adult signatures: second molars / wisdom teeth and broad permanent coverage.
        bool hasAdultSignature = teeth.Any(t => t.FdiNumber is 17 or 18 or 27 or 28 or 37 or 38 or 47 or 48);
        bool broadAdultCoverage = permanentCount >= 24;
        bool permanentDominance = permanentCount >= 20 && deciduousCount <= 8;

        return hasAdultSignature || broadAdultCoverage || permanentDominance;
    }

    private static int MapDeciduousToPermanentCounterpart(int fdi)
    {
        if (!IsDeciduousFdi(fdi))
            return fdi;

        int quadrant = fdi / 10;
        int unit = fdi % 10;
        int permanentQuadrant = quadrant - 4;
        return (permanentQuadrant * 10) + unit;
    }

    private static bool IsDisplayableFdi(int fdi) => IsPermanentFdi(fdi) || IsDeciduousFdi(fdi);

    private static bool IsPermanentFdi(int fdi)
    {
        int quadrant = fdi / 10;
        int unit = fdi % 10;
        return quadrant is >= 1 and <= 4 && unit is >= 1 and <= 8;
    }

    private static bool IsDeciduousFdi(int fdi)
    {
        int quadrant = fdi / 10;
        int unit = fdi % 10;
        return quadrant is >= 5 and <= 8 && unit is >= 1 and <= 5;
    }

    private static bool TryRecoverSingleMissingToothByNeighborGeometry(
        IReadOnlyCollection<DetectedTooth> currentTeeth,
        IReadOnlyCollection<DetectedTooth> rawTeeth,
        IReadOnlyCollection<DetectedPathology> rawPathologies,
        out int recoveredMissingFdi,
        out DetectedTooth recoveredTooth)
    {
        recoveredMissingFdi = 0;
        recoveredTooth = null!;

        if (currentTeeth.Count != 31)
            return false;

        var existingPermanent = currentTeeth
            .Select(t => t.FdiNumber)
            .Where(IsPermanentFdi)
            .ToHashSet();
        var allPermanent = new HashSet<int>(
            Enumerable.Range(11, 8)
                .Concat(Enumerable.Range(21, 8))
                .Concat(Enumerable.Range(31, 8))
                .Concat(Enumerable.Range(41, 8)));

        var missing = allPermanent.Except(existingPermanent).OrderBy(x => x).ToList();
        if (missing.Count != 1)
            return false;

        int missingFdi = missing[0];
        int quadrant = missingFdi / 10;
        int unit = missingFdi % 10;
        if (unit <= 1 || unit >= 8)
            return false;

        int neighborA = quadrant * 10 + (unit - 1);
        int neighborB = quadrant * 10 + (unit + 1);

        var byFdi = currentTeeth
            .Where(t => t.FdiNumber > 0)
            .GroupBy(t => t.FdiNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Confidence).First());

        if (!byFdi.TryGetValue(neighborA, out var toothA) || !byFdi.TryGetValue(neighborB, out var toothB))
            return false;

        float aCx = toothA.X + toothA.Width / 2f;
        float aCy = toothA.Y + toothA.Height / 2f;
        float bCx = toothB.X + toothB.Width / 2f;
        float bCy = toothB.Y + toothB.Height / 2f;
        float centerDist = Distance(aCx, aCy, bCx, bCy);
        float avgWidth = (toothA.Width + toothB.Width) / 2f;

        // Plausible one-tooth gap geometry.
        // Distal molar slots (unit=7) can be more distorted in panoramic views.
        float minGapFactor = unit == 7 ? 1.05f : 1.2f;
        float maxGapFactor = unit == 7 ? 6.0f : 3.8f;
        if (centerDist < avgWidth * minGapFactor || centerDist > avgWidth * maxGapFactor)
            return false;
        float maxNeighborYDelta = unit == 7 ? 0.22f : 0.14f;
        if (Math.Abs(aCy - bCy) > maxNeighborYDelta)
            return false;

        float expectedCx = (aCx + bCx) / 2f;
        float expectedCy = (aCy + bCy) / 2f;
        
        // 1) Prefer a real raw candidate near the expected missing slot (even if mislabelled).
        float maxCandidateDist = Math.Clamp(Math.Max(avgWidth * 1.35f, centerDist * 0.55f), 0.03f, 0.12f);
        float avgHeight = (toothA.Height + toothB.Height) / 2f;
        bool HasNearSlotRawSignal(DetectedTooth t)
        {
            var cx = t.X + t.Width / 2f;
            var cy = t.Y + t.Height / 2f;
            return Distance(cx, cy, expectedCx, expectedCy) <= (maxCandidateDist * 1.25f) && t.Confidence >= 0.02f;
        }

        bool slotSupportExists = rawTeeth
            .Where(IsValidNormalizedBox)
            .Where(t => IsPermanentFdi(t.FdiNumber))
            .Where(t => (t.FdiNumber / 10) == quadrant)
            .Where(HasNearSlotRawSignal)
            .Any(t => !currentTeeth.Any(s =>
                s.FdiNumber == t.FdiNumber &&
                ForensicHeuristicsService.CalculateIoU(
                    s.X, s.Y, s.Width, s.Height,
                    t.X, t.Y, t.Width, t.Height) >= 0.995f));

        var candidate = rawTeeth
            .Where(IsValidNormalizedBox)
            .Where(t => IsPermanentFdi(t.FdiNumber))
            .Where(t => (t.FdiNumber / 10) == quadrant)
            // Don't reuse the exact already-selected tooth box as a fake "new" tooth.
            .Where(t => !currentTeeth.Any(s =>
                s.FdiNumber == t.FdiNumber &&
                ForensicHeuristicsService.CalculateIoU(
                    s.X, s.Y, s.Width, s.Height,
                    t.X, t.Y, t.Width, t.Height) >= 0.995f))
            .Select(t => new
            {
                Tooth = t,
                Dist = Distance(t.X + t.Width / 2f, t.Y + t.Height / 2f, expectedCx, expectedCy),
                FdiPenalty = t.FdiNumber == missingFdi ? 0 : (t.FdiNumber == neighborA || t.FdiNumber == neighborB ? 1 : 2)
            })
            .Where(x => x.Dist <= maxCandidateDist && x.Tooth.Confidence >= 0.04f)
            .Where(x => x.Tooth.Width >= avgWidth * 0.45f && x.Tooth.Width <= avgWidth * 1.90f)
            .Where(x => x.Tooth.Height >= avgHeight * 0.60f && x.Tooth.Height <= avgHeight * 1.45f)
            .OrderBy(x => x.FdiPenalty)
            .ThenBy(x => x.Dist)
            .ThenByDescending(x => x.Tooth.Confidence)
            .FirstOrDefault();

        // If there is strong "missing tooth" evidence and no sufficiently reliable raw candidate,
        // keep the tooth as missing.
        bool hasStrongMissingEvidence = rawPathologies.Any(p =>
            IsMissingTeethClass(p.ClassName) &&
            p.Confidence >= 0.80f &&
            (
                (p.ToothNumber.HasValue && p.ToothNumber.Value == missingFdi) ||
                Distance(p.X + p.Width / 2f, p.Y + p.Height / 2f, expectedCx, expectedCy) <= 0.11f
            ));
        if (hasStrongMissingEvidence && (candidate == null || candidate.Tooth.Confidence < 0.15f))
            return false;

        if (candidate != null)
        {
            float candidateCx = candidate.Tooth.X + candidate.Tooth.Width / 2f;
            float candidateCy = candidate.Tooth.Y + candidate.Tooth.Height / 2f;
            float recoveredWidth = Math.Clamp(candidate.Tooth.Width, avgWidth * 0.60f, avgWidth * 1.35f);
            float recoveredHeight = Math.Clamp(candidate.Tooth.Height, avgHeight * 0.75f, avgHeight * 1.25f);
            float recoveredX = Math.Clamp(candidateCx - recoveredWidth / 2f, 0f, 1f - recoveredWidth);
            float recoveredY = Math.Clamp(candidateCy - recoveredHeight / 2f, 0f, 1f - recoveredHeight);

            recoveredTooth = new DetectedTooth
            {
                FdiNumber = missingFdi,
                Confidence = Math.Clamp(candidate.Tooth.Confidence * 0.90f, 0.08f, 0.60f),
                X = recoveredX,
                Y = recoveredY,
                Width = recoveredWidth,
                Height = recoveredHeight,
                Outline = candidate.Tooth.Outline?.Select(p => (X: p.X, Y: p.Y)).ToList()
            };
            recoveredMissingFdi = missingFdi;
            return true;
        }

        // Geometry-only fallback (strict): if a single tooth slot is clearly present between neighbors
        // and there is no strong "missing tooth" evidence, infer a low-confidence tooth.
        float widthRatio = centerDist / Math.Max(avgWidth, 1e-4f);
        float minFallbackRatio = unit == 7 ? 1.35f : 1.8f;
        float maxFallbackRatio = unit == 7 ? 6.4f : 3.2f;
        float maxFallbackYDelta = unit == 7 ? 0.16f : 0.08f;
        bool plausibleHiddenToothGap =
            widthRatio >= minFallbackRatio &&
            widthRatio <= maxFallbackRatio &&
            Math.Abs(aCy - bCy) <= maxFallbackYDelta;

        if (!hasStrongMissingEvidence && plausibleHiddenToothGap && slotSupportExists)
        {
            float recoveredWidth = Math.Clamp(avgWidth * 0.92f, 0.02f, 0.09f);
            float recoveredHeight = Math.Clamp(avgHeight * 0.96f, 0.05f, 0.20f);
            float recoveredX = Math.Clamp(expectedCx - recoveredWidth / 2f, 0f, 1f - recoveredWidth);
            float recoveredY = Math.Clamp(expectedCy - recoveredHeight / 2f, 0f, 1f - recoveredHeight);

            recoveredTooth = new DetectedTooth
            {
                FdiNumber = missingFdi,
                Confidence = 0.08f,
                X = recoveredX,
                Y = recoveredY,
                Width = recoveredWidth,
                Height = recoveredHeight
            };
            recoveredMissingFdi = missingFdi;
            return true;
        }

        // No candidate and no safe geometric inference: keep the tooth as missing.
        return false;
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2;
        float dy = y1 - y2;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool TryGetSingleMissingPermanentFdi(
        IReadOnlyCollection<DetectedTooth> currentTeeth,
        out int missingFdi)
    {
        missingFdi = 0;

        var existingPermanent = currentTeeth
            .Select(t => t.FdiNumber)
            .Where(IsPermanentFdi)
            .ToHashSet();
        var allPermanent = new HashSet<int>(
            Enumerable.Range(11, 8)
                .Concat(Enumerable.Range(21, 8))
                .Concat(Enumerable.Range(31, 8))
                .Concat(Enumerable.Range(41, 8)));

        var missing = allPermanent.Except(existingPermanent).OrderBy(x => x).ToList();
        if (missing.Count != 1)
            return false;

        missingFdi = missing[0];
        return true;
    }

    private static bool IsMissingTeethClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        var normalized = className.Trim();
        return normalized.Equals("Missing teeth", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Missing_Tooth", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Missing tooth", StringComparison.OrdinalIgnoreCase);
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
                Height = t.Height,
                Outline = t.Outline?.Select(p => (X: p.X, Y: p.Y)).ToList()
            }).ToList(),
            Pathologies = sourcePathologies.Select(p => new DetectedPathology
            {
                ClassName = p.ClassName,
                Confidence = p.Confidence,
                ToothNumber = p.ToothNumber,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                Outline = p.Outline?.Select(p => (X: p.X, Y: p.Y)).ToList()
            }).ToList(),
            RawTeeth = sourceRawTeeth.Select(t => new DetectedTooth
            {
                FdiNumber = t.FdiNumber,
                Confidence = t.Confidence,
                X = t.X,
                Y = t.Y,
                Width = t.Width,
                Height = t.Height,
                Outline = t.Outline?.Select(p => (X: p.X, Y: p.Y)).ToList()
            }).ToList(),
            RawPathologies = sourceRawPathologies.Select(p => new DetectedPathology
            {
                ClassName = p.ClassName,
                Confidence = p.Confidence,
                ToothNumber = p.ToothNumber,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                Outline = p.Outline?.Select(p => (X: p.X, Y: p.Y)).ToList()
            }).ToList(),
            EstimatedAgeRange = source.EstimatedAgeRange,
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


