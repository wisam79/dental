---
name: analyze_evidence
description: Analyze dental X-ray images to detect teeth, pathologies, and estimate age/gender.
---

# Analyze Evidence Skill

Use this skill to perform forensic analysis on dental X-ray images. This involves detecting teeth (FDI notation), identifying pathologies, and estimating biological profile (age/gender).

## Dependencies
- `DentalID.Core.Interfaces.IAiPipelineService` (The AI Engine)
- `DentalID.Application.Services.ForensicAnalysisService` (High-level orchestration)
- `DentalID.Core.DTOs.AnalysisResult` (The output format)

## Workflow

1.  **Validate Input**:
    - Ensure the file is a supported image format (`.png`, `.jpg`, `.bmp`, `.dcm`).
    - **SECURITY**: Do NOT pass file paths directly to the AI engine if possible. Open a `FileStream` (Read-only) and pass the `Stream`. This ensures file locks and prevents race conditions.

2.  **Run Analysis**:
    - Call `IAiPipelineService.AnalyzeImageAsync(stream)`.
    - **Note**: This method is computationally expensive.

3.  **Interpret Results**:
    - **Teeth**: Check `AnalysisResult.Teeth`.
    - **Pathologies**: Check `AnalysisResult.Pathologies`.
    - **Demographics**: Check `EstimatedAge` and `EstimatedGender`.

4.  **Advanced: Batch Processing**:
    - When processing multiple images (e.g., a full case folder), use `Task.WhenAll` to maximize CPU/GPU usage.
    - **Memory Warning**: Process in chunks of 5-10 images to avoid OutOfMemory exceptions on standard laptops.

5.  **Error Recovery**:
    - **Low Confidence**: If `Teeth.Count < 5`, the image might be blurry.
        - *Action*: Apply `Histogram Equalization` (using OpenCV/Skia) and retry.
    - **Upside Down**: If `11` is found at the bottom of the image.
        - *Action*: Rotate 180 degrees and retry.

## Example Usage (C#)

```csharp
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;

public async Task<AnalysisResult> PerformForensicAnalysis(string imagePath, IAiPipelineService aiService)
{
    // 1. Secure Open
    using var stream = File.OpenRead(imagePath);
    
    // 2. Analyze with Retry Logic
    try 
    {
        return await aiService.AnalyzeImageAsync(stream);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] First pass failed: {ex.Message}. Retrying with enhancement...");
        // TODO: call ImagePreprocessor.Enhance(stream)
        return await aiService.AnalyzeImageAsync(stream); // Retry
    }
}
```

## Troubleshooting
- **Zero Teeth Detected**: The image might be upside down or have massive artifacts. Try `DentalID.Core.Utilities.ImagePreprocessor.EnhanceContrast`.
- **"Model Not Found"**: Ensure the `models/` directory contains `teeth_detect.onnx` and `pathology_detect.onnx`.
