using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DentalID.Application.Interfaces;

public interface ITensorPreparationService
{
    /// <summary>
    /// Prepares a tensor for YOLO detection (NCHW, normalized 0-1, padded).
    /// </summary>
    (DenseTensor<float> Tensor, float Scale, float PadX, float PadY) PrepareDetectionTensor(SKBitmap bitmap, int targetSize, float[]? buffer = null);

    /// <summary>
    /// Prepares a tensor for SAM Encoder (HWC? No, standard checks say specific format).
    /// </summary>
    DenseTensor<float> PrepareEncoderTensor(SKBitmap bitmap, int targetSize, float[]? buffer = null);

    /// <summary>
    /// Prepares a tensor for Age/Gender estimation (NCHW, BGR, 0-255).
    /// </summary>
    DenseTensor<float> PrepareAgeGenderTensor(SKBitmap bitmap, int targetSize);
}
