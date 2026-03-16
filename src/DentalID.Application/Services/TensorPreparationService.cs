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

    public unsafe DenseTensor<float> PrepareEncoderTensor(SKBitmap bitmap, int targetSize, float[]? buffer = null, bool applyNormalization = false)
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

        // ImageNet normalization constants
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        if (buffer != null && buffer.Length >= requiredSize)
        {
            // Use provided buffer
            tensor = new DenseTensor<float>(new Memory<float>(buffer, 0, requiredSize), new[] { targetSize, targetSize, 3 });
            
            fixed (float* tPtr = buffer)
            {
                byte* srcPtr = (byte*)finalBitmap.GetPixels().ToPointer();
                int pixelCount = targetSize * targetSize;
                
                // Interleaved RGB (HWC)
                float* ptr = tPtr;

                for (int i = 0; i < pixelCount; i++)
                {
                    float r = srcPtr[i * 4] / 255f;
                    float g = srcPtr[i * 4 + 1] / 255f;
                    float b = srcPtr[i * 4 + 2] / 255f;

                    if (applyNormalization)
                    {
                        ptr[i * 3]     = (r - mean[0]) / std[0];
                        ptr[i * 3 + 1] = (g - mean[1]) / std[1];
                        ptr[i * 3 + 2] = (b - mean[2]) / std[2];
                    }
                    else
                    {
                        ptr[i * 3]     = r;
                        ptr[i * 3 + 1] = g;
                        ptr[i * 3 + 2] = b;
                    }
                }
            }
        }
        else
        {
            // Allocate new tensor
            tensor = new DenseTensor<float>(new[] { targetSize, targetSize, 3 });
            byte* srcPtr = (byte*)finalBitmap.GetPixels().ToPointer();
            
            for (int y = 0; y < targetSize; y++)
            {
                byte* row = srcPtr + (y * targetSize * 4);
                for (int x = 0; x < targetSize; x++)
                {
                    float r = row[x * 4] / 255f;
                    float g = row[x * 4 + 1] / 255f;
                    float b = row[x * 4 + 2] / 255f;

                    if (applyNormalization)
                    {
                        tensor[y, x, 0] = (r - mean[0]) / std[0];
                        tensor[y, x, 1] = (g - mean[1]) / std[1];
                        tensor[y, x, 2] = (b - mean[2]) / std[2];
                    }
                    else
                    {
                        tensor[y, x, 0] = r;
                        tensor[y, x, 1] = g;
                        tensor[y, x, 2] = b;
                    }
                }
            }
        }
        return tensor;
    }

}
