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

public sealed class SamSegmentationService : ISamSegmentationService
{
    private readonly IOnnxSessionManager _sessions;
    private readonly ILogger<SamSegmentationService> _logger;

    public SamSegmentationService(IOnnxSessionManager sessions, ILogger<SamSegmentationService> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void SegmentTeeth(SKBitmap bitmap, IEnumerable<DetectedTooth> teeth)
    {
        if (!_sessions.IsReady || _sessions.SamEncoder == null || _sessions.SamDecoder == null)
            return;

        try
        {
            // 1. Prepare image and get embedding
            // MobileSAM encoder expects 1x3x1024x1024 float tensor
            var imageEmbedding = GetImageEmbedding(bitmap);
            if (imageEmbedding == null) return;

            // 2. Decode mask for each tooth using its bounding box as prompt
            foreach (var tooth in teeth)
            {
                var mask = GetMaskFromBox(imageEmbedding, bitmap.Width, bitmap.Height, tooth.X, tooth.Y, tooth.Width, tooth.Height);
                if (mask != null)
                {
                    tooth.Outline = ExtractContour(mask, tooth.X, tooth.Y, tooth.Width, tooth.Height);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to segment teeth using SAM.");
        }
    }

    public void SegmentPathologies(SKBitmap bitmap, IEnumerable<DetectedPathology> pathologies)
    {
        if (!_sessions.IsReady || _sessions.SamEncoder == null || _sessions.SamDecoder == null)
            return;

        try
        {
            var imageEmbedding = GetImageEmbedding(bitmap);
            if (imageEmbedding == null) return;

            foreach (var path in pathologies)
            {
                var mask = GetMaskFromBox(imageEmbedding, bitmap.Width, bitmap.Height, path.X, path.Y, path.Width, path.Height);
                if (mask != null)
                {
                    path.Outline = ExtractContour(mask, path.X, path.Y, path.Width, path.Height);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to segment pathologies using SAM.");
        }
    }

    private DenseTensor<float>? GetImageEmbedding(SKBitmap original)
    {
        // Resize to 1024x1024
        using var resized = new SKBitmap(1024, 1024);
        original.ScalePixels(resized, SKFilterQuality.Medium);

        var tensor = new DenseTensor<float>(new[] { 1, 3, 1024, 1024 });
        
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
                    int b = row[x * 4 + 0];
                    int g = row[x * 4 + 1];
                    int r = row[x * 4 + 2];

                    tensor[0, 0, y, x] = ((r / 255f) - mean[0]) / std[0];
                    tensor[0, 1, y, x] = ((g / 255f) - mean[1]) / std[1];
                    tensor[0, 2, y, x] = ((b / 255f) - mean[2]) / std[2];
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

        // Box prompt expected format: 1xNx2 point coords, 1xN point labels
        // top-left (label 2), bottom-right (label 3)
        var pointCoords = new DenseTensor<float>(new[] { 1, 2, 2 });
        pointCoords[0, 0, 0] = x1;
        pointCoords[0, 0, 1] = y1;
        pointCoords[0, 1, 0] = x2;
        pointCoords[0, 1, 1] = y2;

        var pointLabels = new DenseTensor<float>(new[] { 1, 2 });
        pointLabels[0, 0] = 2; // top left wrapper
        pointLabels[0, 1] = 3; // bottom right wrapper

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
            // Outputs: masks (1x1xH_origxW_orig), iou_predictions, low_res_masks
            // The decoder usually scales the output mask to orig_im_size automatically.
            var maskTensor = results.FirstOrDefault(r => r.Name == "masks")?.AsTensor<float>();
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
        // Simple bounding edge extractor along the mask
        // Returns coordinates normalized to 0..1 (relative to the full image)
        int h = mask.GetLength(0);
        int w = mask.GetLength(1);

        var topPoints = new List<(int x, int y)>();
        var bottomPoints = new List<(int x, int y)>();
        
        // Scan each column for top and bottom edge
        for (int x = 0; x < w; x++)
        {
            int topY = -1;
            int bottomY = -1;

            for (int y = 0; y < h; y++)
            {
                if (mask[y, x] > 0.5f)
                {
                    if (topY == -1) topY = y;
                    bottomY = y;
                }
            }

            if (topY != -1)
            {
                topPoints.Add((x, topY));
                if (bottomY != topY) bottomPoints.Add((x, bottomY));
            }
        }

        // Combine to form a rough polygon
        var outline = new List<(float X, float Y)>();
        
        // Top edge left to right
        foreach (var pt in topPoints)
        {
            outline.Add(((float)pt.x / w, (float)pt.y / h));
        }

        // Bottom edge right to left
        bottomPoints.Reverse();
        foreach (var pt in bottomPoints)
        {
            outline.Add(((float)pt.x / w, (float)pt.y / h));
        }

        // Safety: If extraction completely fails, return the bounding box
        if (outline.Count < 3)
        {
            return new List<(float X, float Y)>
            {
                (boxNormX, boxNormY),
                (boxNormX + boxNormW, boxNormY),
                (boxNormX + boxNormW, boxNormY + boxNormH),
                (boxNormX, boxNormY + boxNormH)
            };
        }

        return outline;
    }
}
