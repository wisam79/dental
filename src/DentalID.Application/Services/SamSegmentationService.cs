using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DentalID.Application.Services;

public sealed class SamSegmentationService : ISamSegmentationService, IDisposable
{
    private readonly IOnnxSessionManager _sessions;
    private readonly ILogger<SamSegmentationService> _logger;
    private const int MaxTeethToSegment = 24; // Faster processing, usually enough for a jaw
    private const float MinConfidenceToSegment = 0.45f; 

    public SamSegmentationService(IOnnxSessionManager sessions, ILogger<SamSegmentationService> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string DiagLog => Path.Combine(Path.GetTempPath(), "sam_diag.log");
    private void Diag(string msg) { try { File.AppendAllText(DiagLog, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { } }

    // Cached embedding to avoid running encoder twice
    private DenseTensor<float>? _cachedEmbedding;

    public void SegmentTeeth(SKBitmap bitmap, IEnumerable<DetectedTooth> teeth)
    {
        Diag($"SegmentTeeth called. IsReady={_sessions.IsReady}, Encoder={_sessions.SamEncoder != null}, Decoder={_sessions.SamDecoder != null}");
        if (!_sessions.IsReady || _sessions.SamEncoder == null || _sessions.SamDecoder == null)
        {
            Diag("SKIPPED: sessions not ready");
            return;
        }

        try
        {
            // Reset cache at start of new image to prevent stale data usage
            _cachedEmbedding = GetImageEmbedding(bitmap);
            if (_cachedEmbedding == null)
            {
                Diag("GetImageEmbedding returned null");
                return;
            }

            // Limit to top-confidence teeth, prioritized by biometric value (Implants/Crowns first)
            var teethList = teeth
                .Where(t => t.Confidence >= MinConfidenceToSegment && t.Width * t.Height >= 0.001f)
                .OrderByDescending(GetBiometricPriority)
                .ThenByDescending(t => t.Confidence)
                .Take(MaxTeethToSegment).ToList();
            Diag($"Embedding OK. Segmenting {teethList.Count}/{teeth.Count()} teeth...");

            foreach (var tooth in teethList)
            {
                var mask = GetMaskFromBox(_cachedEmbedding, bitmap.Width, bitmap.Height, tooth.X, tooth.Y, tooth.Width, tooth.Height);
                if (mask != null)
                {
                    tooth.Outline = ExtractContour(mask, tooth.X, tooth.Y, tooth.Width, tooth.Height);
                    if (tooth.Outline?.Count >= 3)
                    {
                        var xs = tooth.Outline.Select(p => p.X).ToList();
                        var ys = tooth.Outline.Select(p => p.Y).ToList();
                        tooth.MaskWidth = xs.Max() - xs.Min();
                        tooth.MaskHeight = ys.Max() - ys.Min();

                        float area = 0;
                        int n = tooth.Outline.Count;
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n;
                            area += tooth.Outline[i].X * tooth.Outline[j].Y;
                            area -= tooth.Outline[j].X * tooth.Outline[i].Y;
                        }
                        tooth.MaskArea = Math.Abs(area) / 2.0f;
                    }
                }
            }
            Diag($"Teeth segmentation complete.");
        }
        catch (Exception ex)
        {
            Diag($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "Failed to segment teeth using SAM: {Message}", ex.Message);
            // Ensure cleanup on failure
            _cachedEmbedding = null;
        }
    }

    public void SegmentPathologies(SKBitmap bitmap, IEnumerable<DetectedPathology> pathologies)
    {
        if (!_sessions.IsReady || _sessions.SamDecoder == null)
            return;

        try
        {
            // If SegmentTeeth wasn't called (or failed), generate embedding here
            if (_cachedEmbedding == null && _sessions.SamEncoder != null)
            {
                _cachedEmbedding = GetImageEmbedding(bitmap);
            }

            if (_cachedEmbedding == null) return;

            // Limit pathologies to top 12 for performance, skip tiny boxes
            var pathList = pathologies
                .Where(p => p.Confidence >= MinConfidenceToSegment && p.Width * p.Height >= 0.0005f)
                .OrderByDescending(p => p.Confidence)
                .Take(12).ToList();
            Diag($"Segmenting {pathList.Count}/{pathologies.Count()} pathologies...");

            foreach (var path in pathList)
            {
                var mask = GetMaskFromBox(_cachedEmbedding, bitmap.Width, bitmap.Height, path.X, path.Y, path.Width, path.Height);
                if (mask != null)
                {
                    path.Outline = ExtractContour(mask, path.X, path.Y, path.Width, path.Height);
                    if (path.Outline?.Count >= 3)
                    {
                        var xs = path.Outline.Select(p => p.X).ToList();
                        var ys = path.Outline.Select(p => p.Y).ToList();
                        path.MaskWidth = xs.Max() - xs.Min();
                        path.MaskHeight = ys.Max() - ys.Min();

                        float area = 0;
                        int n = path.Outline.Count;
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n;
                            area += path.Outline[i].X * path.Outline[j].Y;
                            area -= path.Outline[j].X * path.Outline[i].Y;
                        }
                        path.MaskArea = Math.Abs(area) / 2.0f;
                    }
                }
            }
            Diag("Pathology segmentation complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to segment pathologies using SAM.");
        }
        finally
        {
            // Clear cached embedding to free memory and prevent cross-image corruption
            _cachedEmbedding = null;
        }
    }

    public void Dispose()
    {
        _cachedEmbedding = null;
    }


    private static int GetBiometricPriority(DetectedTooth tooth)
    {
        // Future: Check for Implants/Crowns if pathology data is available here.
        // For now, prioritize larger teeth (molars) as they carry more unique surface geometry.
        float area = tooth.Width * tooth.Height;
        if (area > 0.04f) return 10; // Molars
        if (area > 0.02f) return 5;  // Premolars/Canines
        return 1; // Incisors
    }

    private DenseTensor<float>? GetImageEmbedding(SKBitmap original)
    {
        // Resize to 1024x1024 - Explicitly use Rgba8888 to ensure consistent channel order across platforms
        using var resized = new SKBitmap(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Opaque);
        original.ScalePixels(resized, SKFilterQuality.Medium);

        // SAM encoder expects HWC format: [1024, 1024, 3]
        var tensor = new DenseTensor<float>(new[] { 1024, 1024, 3 });
        
        // Normalize (Standard ImageNet means/stds)
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        unsafe
        {
            byte* ptr = (byte*)resized.GetPixels().ToPointer();
            int width = 1024;
            int height = 1024;
            int rowBytes = resized.RowBytes;

            for (int y = 0; y < height; y++)
            {
                byte* row = ptr + y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    // Rgba8888: R=0, G=1, B=2, A=3
                    float r = row[x * 4 + 0] / 255f;
                    float g = row[x * 4 + 1] / 255f;
                    float b = row[x * 4 + 2] / 255f;

                    tensor[y, x, 0] = (r - mean[0]) / std[0];
                    tensor[y, x, 1] = (g - mean[1]) / std[1];
                    tensor[y, x, 2] = (b - mean[2]) / std[2];
                }
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_sessions.SamEncoderInputName, tensor)
        };

        using var results = _sessions.SamEncoder!.Run(inputs);
        var embedding = results.FirstOrDefault()?.AsTensor<float>() as DenseTensor<float>;
        
        // Return a copy to avoid ObjectDisposedException
        if (embedding == null) return null;
        var copy = new DenseTensor<float>(embedding.Dimensions.ToArray());
        embedding.Buffer.CopyTo(copy.Buffer);
        return copy;
    }

    private float[,]? GetMaskFromBox(DenseTensor<float> imageEmbedding, int origW, int origH, float normX, float normY, float normW, float normH)
    {
        // Convert normalized bbox to 1024x1024 coordinate space
        float x1 = normX * 1024;
        float y1 = normY * 1024;
        float x2 = (normX + normW) * 1024;
        float y2 = (normY + normH) * 1024;
        float cx = (normX + normW / 2) * 1024;
        float cy = (normY + normH / 2) * 1024;

        // Box prompt + Center point prompt to help SAM find the organ's boundary
        // top-left (label 2), bottom-right (label 3), center (label 1)
        var pointCoords = new DenseTensor<float>(new[] { 1, 3, 2 });
        pointCoords[0, 0, 0] = x1;
        pointCoords[0, 0, 1] = y1;
        pointCoords[0, 1, 0] = x2;
        pointCoords[0, 1, 1] = y2;
        pointCoords[0, 2, 0] = cx;
        pointCoords[0, 2, 1] = cy;

        var pointLabels = new DenseTensor<float>(new[] { 1, 3 });
        pointLabels[0, 0] = 2; // top left corner
        pointLabels[0, 1] = 3; // bottom right corner
        pointLabels[0, 2] = 1; // foreground point (center)

        var maskInput = new DenseTensor<float>(new[] { 1, 1, 256, 256 }); // Zeros
        var hasMaskInput = new DenseTensor<float>(new[] { 1 });
        hasMaskInput[0] = 0;

        var origImgSize = new DenseTensor<float>(new[] { 2 });
        origImgSize[0] = origH;
        origImgSize[1] = origW;

        var inputs = new List<NamedOnnxValue>
        {
            // Note: input names vary by specific ONNX export. Using standard SAM names.
            NamedOnnxValue.CreateFromTensor("image_embeddings", imageEmbedding),
            NamedOnnxValue.CreateFromTensor("point_coords", pointCoords),
            NamedOnnxValue.CreateFromTensor("point_labels", pointLabels),
            NamedOnnxValue.CreateFromTensor("mask_input", maskInput),
            NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskInput),
            NamedOnnxValue.CreateFromTensor("orig_im_size", origImgSize)
        };

        try
        {
            using var results = _sessions.SamDecoder!.Run(inputs);
            // Get first output tensor (masks) — name varies by ONNX export version
            var firstResult = results.FirstOrDefault();
            if (firstResult == null) return null;
            
            var maskTensor = firstResult.AsTensor<float>();
            if (maskTensor == null) return null;

            int h = maskTensor.Dimensions[2];
            int w = maskTensor.Dimensions[3];
            var mask2d = new float[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Threshold at 0.0 (logits)
                    mask2d[y, x] = maskTensor[0, 0, y, x] > 0.0f ? 1.0f : 0.0f;
                }
            }
            return mask2d;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"SAM Decoder Run failed: {ex.Message}");
            return null;
        }
    }

    private List<(float X, float Y)> ExtractContour(float[,] mask, float boxNormX, float boxNormY, float boxNormW, float boxNormH)
    {
        int h = mask.GetLength(0);
        int w = mask.GetLength(1);

        // Clip scanning region to bounding box + medium margin (10%)
        float margin = 0.10f;
        int startX = Math.Max(0, (int)((boxNormX - margin) * w));
        int endX   = Math.Min(w - 1, (int)((boxNormX + boxNormW + margin) * w));
        int startY = Math.Max(0, (int)((boxNormY - margin) * h));
        int endY   = Math.Min(h - 1, (int)((boxNormY + boxNormH + margin) * h));

        // Dual-pass scan (Columns + Rows) to handle impacted/horizontal teeth
        var points = new HashSet<(int x, int y)>();

        // 1. Column-wise scan (Top/Bottom edges) - Higher density scan (80 steps)
        int xStep = Math.Max(1, (endX - startX) / 80);
        for (int x = startX; x <= endX; x += xStep)
        {
            int firstY = -1, lastY = -1;
            for (int y = startY; y <= endY; y++)
            {
                if (mask[y, x] > 0.1f) // Lower threshold for sensitivity
                {
                    if (firstY == -1) firstY = y;
                    lastY = y;
                }
            }
            if (firstY != -1) { points.Add((x, firstY)); points.Add((x, lastY)); }
        }

        // 2. Row-wise scan (Left/Right edges) - Higher density scan (80 steps)
        int yStep = Math.Max(1, (endY - startY) / 80);
        for (int y = startY; y <= endY; y += yStep)
        {
            int firstX = -1, lastX = -1;
            for (int x = startX; x <= endX; x++)
            {
                if (mask[y, x] > 0.1f) // Lower threshold for sensitivity
                {
                    if (firstX == -1) firstX = x;
                    lastX = x;
                }
            }
            if (firstX != -1) { points.Add((firstX, y)); points.Add((lastX, y)); }
        }

        // If the mask yielded no outline points (e.g. low confidence mask), fallback to bounding box
        if (points.Count == 0)
        {
            return new List<(float X, float Y)>
            {
                (boxNormX, boxNormY),
                (boxNormX + boxNormW, boxNormY),
                (boxNormX + boxNormW, boxNormY + boxNormH),
                (boxNormX, boxNormY + boxNormH)
            };
        }

        // Sort points to form a convex-ish hull or simple clockwise outline
        float cx = (float)points.Average(p => p.x);
        float cy = (float)points.Average(p => p.y);
        
        var sorted = points
            .Select(p => new { p.x, p.y, Angle = Math.Atan2(p.y - cy, p.x - cx) })
            .OrderBy(p => p.Angle)
            .Select(p => ((float)p.x / w, (float)p.y / h))
            .ToList();

        if (sorted.Count < 3)
        {
            return new List<(float X, float Y)>
            {
                (boxNormX, boxNormY),
                (boxNormX + boxNormW, boxNormY),
                (boxNormX + boxNormW, boxNormY + boxNormH),
                (boxNormX, boxNormY + boxNormH)
            };
        }

        return sorted;
    }
}
