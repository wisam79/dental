using DentalID.Core.Entities;

namespace DentalID.Core.DTOs;

public class MatchCandidate
{
    public Subject Subject { get; set; } = null!;
    public double Score { get; set; }
    public string MatchMethod { get; set; } = "Unknown";
    public Dictionary<string, double> MatchDetails { get; set; } = new();
    public int? MatchId { get; set; }
}
