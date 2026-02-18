using DentalID.Core.DTOs;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Spatial refinement of FDI tooth numbering based on X/Y positions.
/// </summary>
public interface IFdiSpatialService
{
    /// <summary>
    /// Refines FDI numbering by spatial analysis (quadrant detection, arch sorting).
    /// </summary>
    List<DetectedTooth> RefineFdiNumbering(List<DetectedTooth> teeth);
}
