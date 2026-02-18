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
                // Apply dynamic filtering based on user sensitivity
                UpdateForensicFilter(result, sensitivity);
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

        // Bug #18 fix: Guard against null RawTeeth to avoid silently clearing result.Teeth
        var rawTeeth = result.RawTeeth ?? result.Teeth ?? new List<DetectedTooth>();
        result.Teeth = rawTeeth
            .Where(t => t.Confidence >= baseThreshold)
            .ToList();

        // Use raw pathologies when available, otherwise fallback to current set.
        var rawPathologies = result.RawPathologies ?? result.Pathologies ?? new List<DetectedPathology>();

        // 3. Filter Pathologies from raw list with class bias
        result.Pathologies = rawPathologies
            .Where(p => 
            {
                double classBias = GetClassBias(p.ClassName);
                return p.Confidence >= (baseThreshold + classBias);
            })
            .ToList();
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
            if (string.IsNullOrWhiteSpace(keySource) || keySource == "ChangeMeToASecureRandomKey")
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
}
