using System;
using DentalID.Application.Interfaces;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DentalID.Application.Services;

public class TensorPreparationService : ITensorPreparationService
{
    public unsafe (DenseTensor<float> Tensor, float Scale, float PadX, float PadY) PrepareDetectionTensor(SKBitmap bitmap, int targetSize, float[]? buffer = null)
    {
        float scale = Math.Min((float)targetSize / bitmap.Width, (float)targetSize / bitmap.Height);
        int newWidth = (int)(bitmap.Width * scale);
        int newHeight = (int)(bitmap.Height * scale);
        float padX = (targetSize - newWidth) / 2f;
        float padY = (targetSize - newHeight) / 2f;

        using var finalBitmap = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(finalBitmap))
        {
            canvas.Clear(SKColors.Black);
            var destRect = new SKRect(padX, padY, padX + newWidth, padY + newHeight);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(bitmap, destRect, paint);
        }

        int requiredSize = targetSize * targetSize * 3;
        DenseTensor<float> tensor;
        
        if (buffer != null && buffer.Length >= requiredSize)
        {
            fixed (float* tPtr = buffer)
            {
                 byte* srcPtr = (byte*)finalBitmap.GetPixels().ToPointer();
                 int pixelCount = targetSize * targetSize;
                 
                 float* rPtr = tPtr;
                 float* gPtr = tPtr + pixelCount;
                 float* bPtr = tPtr + (2 * pixelCount);

                 for (int i = 0; i < pixelCount; i++)
                 {
                     rPtr[i] = srcPtr[i * 4] / 255f;
                     gPtr[i] = srcPtr[i * 4 + 1] / 255f;
                     bPtr[i] = srcPtr[i * 4 + 2] / 255f;
                 }
            }
            tensor = new DenseTensor<float>(new Memory<float>(buffer, 0, requiredSize), new[] { 1, 3, targetSize, targetSize });
        }
        else
        {
            tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
            byte* ptr = (byte*)finalBitmap.GetPixels().ToPointer();
            
            for(int y=0; y<targetSize; y++) 
            {
                byte* rowPtr = ptr + (y * targetSize * 4);
                for (int x = 0; x < targetSize; x++)
                {
                    tensor[0, 0, y, x] = rowPtr[x * 4] / 255f;
                    tensor[0, 1, y, x] = rowPtr[x * 4 + 1] / 255f;
                    tensor[0, 2, y, x] = rowPtr[x * 4 + 2] / 255f;
                }
            }
        }
        
        return (tensor, scale, padX, padY);
    }

    public unsafe DenseTensor<float> PrepareEncoderTensor(SKBitmap bitmap, int targetSize, float[]? buffer = null)
    {
        // Encoder model expects HWC [1024, 1024, 3] (Channels Last)
        // Previous error: "index: 2 Got: 1024 Expected: 3" confirms expected shape is [H, W, C]
        
        float scale = Math.Min((float)targetSize / bitmap.Width, (float)targetSize / bitmap.Height);
        int newWidth = (int)(bitmap.Width * scale);
        int newHeight = (int)(bitmap.Height * scale);
        int padX = (targetSize - newWidth) / 2;
        int padY = (targetSize - newHeight) / 2;

        using var finalBitmap = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(finalBitmap))
        {
            canvas.Clear(SKColors.Black);
            var destRect = new SKRect(padX, padY, padX + newWidth, padY + newHeight);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(bitmap, destRect, paint);
        }

        int requiredSize = targetSize * targetSize * 3;
        DenseTensor<float> tensor;

        if (buffer != null && buffer.Length >= requiredSize)
        {
            // Use provided buffer
            tensor = new DenseTensor<float>(new Memory<float>(buffer, 0, requiredSize), new[] { targetSize, targetSize, 3 });
            
            fixed (float* tPtr = buffer)
            {
                byte* srcPtr = (byte*)finalBitmap.GetPixels().ToPointer();
                int pixelCount = targetSize * targetSize;
                
                // Interleaved RGB (HWC)
                // Pixel i: [R, G, B]
                float* ptr = tPtr;

                for (int i = 0; i < pixelCount; i++)
                {
                    ptr[i * 3]     = srcPtr[i * 4] / 255f;     // R
                    ptr[i * 3 + 1] = srcPtr[i * 4 + 1] / 255f; // G
                    ptr[i * 3 + 2] = srcPtr[i * 4 + 2] / 255f; // B
                }
            }
        }
        else
        {
            // Allocate new tensor
            tensor = new DenseTensor<float>(new[] { targetSize, targetSize, 3 });
            byte* srcPtr = (byte*)finalBitmap.GetPixels().ToPointer();
            
            // DenseTensor storage is linear, so we can just write linearly if we match the shape
            // However, using the indexer is safer for clarity, though slower.
            // But since DenseTensor is backed by a linear array in row-major order:
            // [y, x, c] -> y*W*C + x*C + c
            // This matches exactly the interleaved format.
            // So we can optimize by writing directly to the buffer if accessible, 
            // but DenseTensor<T> doesn't expose the pointer easily without unsafe or Memory.
            
            for (int y = 0; y < targetSize; y++)
            {
                byte* row = srcPtr + (y * targetSize * 4);
                for (int x = 0; x < targetSize; x++)
                {
                    tensor[y, x, 0] = row[x * 4] / 255f;     // R
                    tensor[y, x, 1] = row[x * 4 + 1] / 255f; // G
                    tensor[y, x, 2] = row[x * 4 + 2] / 255f; // B
                }
            }
        }
        return tensor;
    }

    public unsafe DenseTensor<float> PrepareAgeGenderTensor(SKBitmap bitmap, int targetSize)
    {
        // InsightFace: BGR, NCHW [-1, 3, 96, 96], 0-255 raw
        using var resized = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(resized))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, targetSize, targetSize), paint);
        }

        var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
        byte* ptr = (byte*)resized.GetPixels().ToPointer();
        for (int y = 0; y < targetSize; y++)
        {
            byte* row = ptr + (y * targetSize * 4);
            for (int x = 0; x < targetSize; x++)
            {
                // SkiaSharp RGBA → flip to BGR
                tensor[0, 0, y, x] = row[x * 4 + 2]; // B
                tensor[0, 1, y, x] = row[x * 4 + 1]; // G
                tensor[0, 2, y, x] = row[x * 4];     // R
            }
        }
        return tensor;
    }
}
