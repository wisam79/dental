using System.Collections.Generic;
using System.Threading.Tasks;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Service for AI-powered chat functionality in the forensic analysis application.
/// Uses LLM for context-aware responses about dental analysis results.
/// </summary>
public interface IAiChatService
{
    /// <summary>
    /// Sends a user message and gets an AI-generated response.
    /// </summary>
    /// <param name="userMessage">The user's question or message</param>
    /// <param name="analysisContext">Context from the current analysis result</param>
    /// <returns>AI-generated response message</returns>
    Task<AiChatResponse> GetResponseAsync(string userMessage, AnalysisContext analysisContext);

    /// <summary>
    /// Generates a comprehensive summary of the analysis results.
    /// </summary>
    /// <param name="analysisContext">Context from the current analysis result</param>
    /// <returns>Comprehensive summary text</returns>
    Task<string> GenerateSummaryAsync(AnalysisContext analysisContext);

    /// <summary>
    /// Generates treatment recommendations based on analysis findings.
    /// </summary>
    /// <param name="analysisContext">Context from the current analysis result</param>
    /// <returns>Treatment recommendations</returns>
    Task<List<TreatmentRecommendation>> GetTreatmentRecommendationsAsync(AnalysisContext analysisContext);

    /// <summary>
    /// Checks if the AI service is available and configured.
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// Context information from analysis results to provide to the AI.
/// </summary>
public class AnalysisContext
{
    public int TeethCount { get; set; }
    public int PathologiesCount { get; set; }
    public int? EstimatedAge { get; set; }
    public string? EstimatedGender { get; set; }
    public double? UniquenessScore { get; set; }
    public List<string> DetectedPathologies { get; set; } = new();
    public List<string> SmartInsights { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public string? FingerprintCode { get; set; }
    public double ProcessingTimeMs { get; set; }
}

/// <summary>
/// AI-generated response with metadata.
/// </summary>
public class AiChatResponse
{
    public string Content { get; set; } = string.Empty;
    public string Confidence { get; set; } = "High";
    public List<string> Sources { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Treatment recommendation with priority and details.
/// </summary>
public class TreatmentRecommendation
{
    public string ToothNumber { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Treatment { get; set; } = string.Empty;
    public string Priority { get; set; } = "Routine";
    public string Prognosis { get; set; } = "Good";
    public string Specialist { get; set; } = string.Empty;
}
