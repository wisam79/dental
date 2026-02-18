using DentalID.Core.Enums;

namespace DentalID.Core.DTOs;

public class MatchingCriteria
{
    public string? Gender { get; set; } // "Male", "Female", or null/empty for Any
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    
    // Future expansion:
    // public List<string> MustHaveFeatures { get; set; }
}
