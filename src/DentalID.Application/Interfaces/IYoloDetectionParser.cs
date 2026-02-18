using DentalID.Core.DTOs;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Parses YOLO model outputs into detection results with NMS.
/// </summary>
public interface IYoloDetectionParser
{
    List<DetectedTooth> ParseTeethDetections(Tensor<float> output, int inputSize, float scale, 
        float padX, float padY, float confidenceThreshold);
    List<DetectedPathology> ParsePathologyDetections(Tensor<float> output, int inputSize, float scale,
        float padX, float padY, float confidenceThreshold, string[] classNames);
    void MapPathologiesToTeeth(List<DetectedTooth> teeth, List<DetectedPathology> pathologies);
    List<T> ApplyNms<T>(List<T> detections, float iouThreshold) where T : class;
}
