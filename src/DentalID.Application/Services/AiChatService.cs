using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DentalID.Core.Interfaces;

namespace DentalID.Application.Services;

/// <summary>
/// AI-powered chat service using LLM for context-aware forensic dental analysis responses.
/// This implementation supports multiple backends: OpenAI GPT, Anthropic Claude, or local LLM.
/// </summary>
public class AiChatService : IAiChatService
{
    private readonly ILoggerService _logger;
    private readonly IAiConfiguration _config;
    private readonly HttpClient _httpClient;
    
    // Retry configuration
    private const int MaxRetries = 3;
    private const int InitialDelayMs = 1000;
    
    // LLM Provider type
    private readonly LlmProvider _provider;
    
    public AiChatService(ILoggerService logger, IAiConfiguration config, HttpClient httpClient)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        
        // Determine provider from configuration
        _provider = config.LlmProvider?.ToLower() switch
        {
            "openai" => LlmProvider.OpenAI,
            "anthropic" => LlmProvider.Anthropic,
            "local" => LlmProvider.Local,
            "grok" => LlmProvider.Grok,
            _ => LlmProvider.RulesBased // Fallback for demo/testing
        };
        
        _logger.LogInformation($"AI Chat Service initialized with provider: {_provider}");
    }

    /// <summary>
    /// Executes an async operation with retry logic and exponential backoff.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        int attempt = 0;
        int delay = InitialDelayMs;
        
        while (true)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransientError(ex))
            {
                attempt++;
                _logger.LogWarning($"{operationName} failed (attempt {attempt}/{MaxRetries}): {ex.Message}. Retrying in {delay}ms...");
                
                if (attempt >= MaxRetries)
                {
                    _logger.LogError(ex, $"{operationName} failed after {MaxRetries} attempts");
                    throw;
                }
                
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay *= 2; // Exponential backoff
            }
        }
    }

    /// <summary>
    /// Determines if an exception is transient and worth retrying.
    /// </summary>
    private bool IsTransientError(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TaskCanceledException ||
               ex is TimeoutException ||
               (ex is OperationCanceledException && !ex.Message.Contains("cancel"));
    }

    public bool IsAvailable => _provider != LlmProvider.RulesBased || _config.EnableRulesBasedFallback;

    public async Task<AiChatResponse> GetResponseAsync(string userMessage, AnalysisContext analysisContext)
    {
        try
        {
            _logger.LogAudit("AI_CHAT_REQUEST", "User", userMessage);
            
            // Build context for the LLM
            var systemPrompt = BuildSystemPrompt(analysisContext);
            var contextJson = SerializeContext(analysisContext);
            
            string response;
            string confidence;
            
            if (_provider == LlmProvider.RulesBased)
            {
                // Use enhanced rules-based as fallback
                var (ruleResponse, ruleConfidence) = await GetRulesBasedResponseAsync(userMessage, analysisContext);
                response = ruleResponse;
                confidence = ruleConfidence;
            }
            else
            {
                // Use actual LLM
                var llmResponse = await CallLlmAsync(systemPrompt, userMessage, contextJson);
                response = llmResponse.Content;
                confidence = llmResponse.Confidence;
            }
            
            _logger.LogAudit("AI_CHAT_RESPONSE", "System", response.Substring(0, Math.Min(100, response.Length)));
            
            return new AiChatResponse
            {
                Content = response,
                Confidence = confidence,
                Sources = ExtractSources(analysisContext),
                Metadata = new Dictionary<string, string>
                {
                    { "provider", _provider.ToString() },
                    { "timestamp", DateTime.UtcNow.ToString("o") },
                    { "model", _config.LlmModel ?? "default" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Chat Service failed");
            
            // Fallback to rules-based on error
            var (fallbackResponse, fallbackConfidence) = await GetRulesBasedResponseAsync(userMessage, analysisContext);
            return new AiChatResponse
            {
                Content = fallbackResponse,
                Confidence = fallbackConfidence,
                Sources = new List<string> { "Fallback Mode" },
                Metadata = new Dictionary<string, string>
                {
                    { "error", ex.Message },
                    { "fallback", "true" }
                }
            };
        }
    }

    public async Task<string> GenerateSummaryAsync(AnalysisContext context)
    {
        if (_provider != LlmProvider.RulesBased)
        {
            var prompt = $"Generate a comprehensive forensic dental analysis summary based on:\n{SerializeContext(context)}";
            var response = await CallLlmAsync(
                "You are a forensic dentist. Provide a detailed analysis summary.",
                prompt,
                SerializeContext(context)
            );
            return response.Content;
        }
        
        // Rules-based summary
        return GenerateRulesBasedSummary(context);
    }

    public async Task<List<TreatmentRecommendation>> GetTreatmentRecommendationsAsync(AnalysisContext context)
    {
        if (_provider != LlmProvider.RulesBased)
        {
            var prompt = $"Generate treatment recommendations based on:\n{SerializeContext(context)}";
            var response = await CallLlmAsync(
                "You are a dental specialist. Provide treatment recommendations in JSON format.",
                prompt,
                SerializeContext(context)
            );
            
            try
            {
                return JsonSerializer.Deserialize<List<TreatmentRecommendation>>(response.Content) 
                       ?? new List<TreatmentRecommendation>();
            }
            catch
            {
                return ParseRecommendationsFromText(response.Content);
            }
        }
        
        return GenerateRulesBasedRecommendations(context);
    }

    private string BuildSystemPrompt(AnalysisContext context)
    {
        return $@"You are an expert forensic dental analyst AI assistant. You have deep knowledge of:
- Dental anatomy and FDI tooth numbering system (ISO 3950)
- Dental pathologies (caries, fillings, crowns, implants, root canals, abscesses)
- Age estimation from dental development stages
- Gender estimation from morphometric analysis
- Forensic odontology and identity matching
- Treatment planning and dental triage

Current Analysis Context:
- Teeth Detected: {context.TeethCount}
- Pathologies Found: {context.PathologiesCount}
- Detected Conditions: {string.Join(", ", context.DetectedPathologies)}
- Estimated Age: {context.EstimatedAge ?? 0}
- Estimated Gender: {context.EstimatedGender ?? "Unknown"}
- Uniqueness Score: {context.UniquenessScore:P0}
- Smart Insights: {string.Join("; ", context.SmartInsights)}
- Processing Time: {context.ProcessingTimeMs:F0}ms

Provide accurate, clinically relevant responses. Always cite specific findings from the analysis.";
    }

    private string SerializeContext(AnalysisContext context)
    {
        return JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = false });
    }

    private async Task<AiChatResponse> CallLlmAsync(string systemPrompt, string userMessage, string contextJson)
    {
        switch (_provider)
        {
            case LlmProvider.OpenAI:
                return await CallOpenAiAsync(systemPrompt, userMessage, contextJson);
            case LlmProvider.Anthropic:
                return await CallAnthropicAsync(systemPrompt, userMessage, contextJson);
            case LlmProvider.Local:
                return await CallLocalLlmAsync(systemPrompt, userMessage, contextJson);
            case LlmProvider.Grok:
                return await CallGrokAsync(systemPrompt, userMessage, contextJson);
            default:
                throw new InvalidOperationException($"Unknown LLM provider: {_provider}");
        }
    }

    private async Task<AiChatResponse> CallOpenAiAsync(string systemPrompt, string userMessage, string contextJson)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var apiKey = _config.LlmApiKey ?? throw new InvalidOperationException("OpenAI API key not configured");
            var model = _config.LlmModel ?? "gpt-4";
            
            var requestBody = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"{userMessage}\n\nAnalysis Data:\n{contextJson}" }
                },
                temperature = 0.3,
                max_tokens = 1000
            };
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions",
                requestBody
            );
            
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OpenAiResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return new AiChatResponse
            {
                Content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response",
                Confidence = "High",
                Sources = new List<string> { $"OpenAI {model}" }
            };
        }, "OpenAI API Call");
    }

    private async Task<AiChatResponse> CallAnthropicAsync(string systemPrompt, string userMessage, string contextJson)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var apiKey = _config.LlmApiKey ?? throw new InvalidOperationException("Anthropic API key not configured");
            var model = _config.LlmModel ?? "claude-3-opus-20240229";
            
            var requestBody = new
            {
                model = model,
                max_tokens = 1000,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = $"{userMessage}\n\nAnalysis Data:\n{contextJson}" } }
            };
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            
            var response = await _httpClient.PostAsJsonAsync(
                "https://api.anthropic.com/v1/messages",
                requestBody
            );
            
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AnthropicResponse>(responseJson);
            
            return new AiChatResponse
            {
                Content = result?.Content?.FirstOrDefault()?.Text ?? "No response",
                Confidence = "High",
                Sources = new List<string> { $"Anthropic {model}" }
            };
        }, "Anthropic API Call");
    }

    private async Task<AiChatResponse> CallLocalLlmAsync(string systemPrompt, string userMessage, string contextJson)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var endpoint = _config.LocalLlmEndpoint ?? "http://localhost:11434/api/chat";
            
            var requestBody = new
            {
                model = _config.LlmModel ?? "llama2",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"{userMessage}\n\nAnalysis Data:\n{contextJson}" }
                },
                stream = false
            };
            
            var response = await _httpClient.PostAsJsonAsync(endpoint, requestBody);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson);
            
            return new AiChatResponse
            {
                Content = result?.Message?.Content ?? "No response",
                Confidence = "Medium",
                Sources = new List<string> { $"Local LLM ({_config.LlmModel})" }
            };
        }, "Local LLM Call");
    }

    private async Task<AiChatResponse> CallGrokAsync(string systemPrompt, string userMessage, string contextJson)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var apiKey = _config.LlmApiKey ?? throw new InvalidOperationException("Grok API key not configured");
            var model = _config.LlmModel ?? "grok-2";
            
            var requestBody = new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"{userMessage}\n\nAnalysis Data:\n{contextJson}" }
                },
                temperature = 0.3,
                max_tokens = 1000
            };
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            var response = await _httpClient.PostAsJsonAsync(
                "https://api.x.ai/v1/chat/completions",
                requestBody
            );
            
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GrokResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return new AiChatResponse
            {
                Content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response",
                Confidence = "High",
                Sources = new List<string> { $"Grok {model}" }
            };
        }, "Grok API Call");
    }

    private async Task<(string Content, string Confidence)> GetRulesBasedResponseAsync(string userMessage, AnalysisContext context)
    {
        var input = userMessage.ToLower();
        
        // Age estimation
        if (input.Contains("age") || input.Contains("old") || input.Contains("how old"))
        {
            if (!context.EstimatedAge.HasValue)
                return ("Age estimation is not available. The AI model could not reliably estimate age from the dental features.", "Low");
            
            var age = context.EstimatedAge.Value;
            string ageRange = age switch
            {
                < 12 => "Child (Primary Dentition)",
                < 18 => "Adolescent (Mixed Dentition)",
                < 25 => "Young Adult (Early Permanent Dentition)",
                < 40 => "Adult (Mature Dentition)",
                < 60 => "Middle-Aged (Adult Dentition)",
                _ => "Senior (Adult Dentition)"
            };
            
            return ($"Based on dental development stages, pulp-tooth ratio, and morphological analysis: " +
                    $"Estimated age: {age} years ({ageRange}). " +
                    $"This assessment considers third molar development, root formation patterns, " +
                    $"and age-related dental changes per established forensic odontology standards.", "High");
        }
        
        // Gender estimation
        if (input.Contains("gender") || input.Contains("sex") || input.Contains("male") || input.Contains("female"))
        {
            if (string.IsNullOrEmpty(context.EstimatedGender))
                return ("Gender estimation is not available.", "Low");
            
            var gender = context.EstimatedGender;
            return ($"Morphometric analysis of mandibular ramus dimensions, " +
                    $"canine tooth size ratios, and overall skeletal pattern suggests: {gender}. " +
                    $"Note: This is probabilistic and should be corroborated with other forensic evidence.", "Medium");
        }
        
        // Pathologies
        if (input.Contains("pathology") || input.Contains("damage") || input.Contains("caries") || 
            input.Contains("lesion") || input.Contains("problem") || input.Contains("issue"))
        {
            if (context.DetectedPathologies.Count == 0)
                return ("No significant pathologies detected in this radiographic analysis. " +
                        "The dentition appears structurally sound with no evidence of caries, " +
                        "periapical lesions, or other pathological conditions.", "High");
            
            var pathologies = string.Join(", ", context.DetectedPathologies);
            var urgent = context.DetectedPathologies.Any(p => 
                p.Contains("Periapical") || p.Contains("Abscess") || p.Contains("Lesion"));
            
            return ($"Detected pathologies: {pathologies}. " +
                    (urgent ? "⚠️ URGENT: Periapical lesions detected - immediate evaluation recommended. " : "") +
                    $"Total: {context.PathologiesCount} anomaly(ies) flagged. " +
                    $"Please refer to the detailed analysis for specific tooth-by-tooth findings.", 
                    urgent ? "High" : "Medium");
        }
        
        // Summary
        if (input.Contains("summary") || input.Contains("report") || input.Contains("overview"))
        {
            return (GenerateRulesBasedSummary(context), "High");
        }
        
        // Treatment
        if (input.Contains("treatment") || input.Contains("recommendation") || input.Contains("what to do"))
        {
            var recommendations = GenerateRulesBasedRecommendations(context);
            if (recommendations.Count == 0)
                return ("No treatment recommendations - the dentition appears healthy. " +
                        "Continue with routine oral hygiene and regular dental checkups.", "High");
            
            var urgent = recommendations.Where(r => r.Priority == "Urgent").ToList();
            var routine = recommendations.Where(r => r.Priority == "Routine").ToList();
            
            var response = "Treatment Recommendations:\n";
            if (urgent.Any())
                response += $"\n🚨 URGENT ({urgent.Count}):\n" + string.Join("\n", urgent.Select(r => $"  - {r.ToothNumber}: {r.Treatment} ({r.Prognosis})"));
            if (routine.Any())
                response += $"\n⚠️ ROUTINE ({routine.Count}):\n" + string.Join("\n", routine.Select(r => $"  - {r.ToothNumber}: {r.Treatment}"));
            
            return (response, "High");
        }
        
        // Identity/Forensics
        if (input.Contains("identity") || input.Contains("match") || input.Contains("forensic") || input.Contains("uniqueness"))
        {
            if (!context.UniquenessScore.HasValue)
                return ("Identity matching data is not available.", "Low");
            
            var score = context.UniquenessScore.Value;
            var interpretation = score switch
            {
                > 0.9 => "Highly unique - Strong forensic match probability",
                > 0.7 => "Moderately unique - Good candidate for identification",
                > 0.5 => "Less unique - Requires additional dental records for confirmation",
                _ => "Common pattern - Limited forensic discrimination"
            };
            
            return ($"Forensic Analysis: Uniqueness Score = {score:P0}. " +
                    $"{interpretation}. " +
                    $"Fingerprint Code: {context.FingerprintCode ?? "N/A"}. " +
                    $"Higher scores indicate more distinctive dental patterns suitable for positive identification.", "High");
        }
        
        // Confidence
        if (input.Contains("confidence") || input.Contains("reliable") || input.Contains("accuracy"))
        {
            var teethConf = context.TeethCount >= 28 ? "High" : context.TeethCount >= 20 ? "Medium" : "Low";
            var pathConf = context.PathologiesCount == 0 ? "High" : context.PathologiesCount < 5 ? "Medium" : "High";
            
            return ($"Analysis Confidence Assessment:\n" +
                    $"- Teeth Detection: {teethConf} ({context.TeethCount} teeth identified)\n" +
                    $"- Pathology Detection: {pathConf} ({context.PathologiesCount} conditions detected)\n" +
                    $"- Age Estimation: {(context.EstimatedAge.HasValue ? "Available" : "Not Available")}\n" +
                    $"- Gender Estimation: {(string.IsNullOrEmpty(context.EstimatedGender) ? "Not Available" : "Available")}\n" +
                    $"- Processing Time: {context.ProcessingTimeMs:F0}ms",
                    teethConf == "High" && pathConf == "High" ? "High" : "Medium");
        }
        
        // Default
        return ("I can help analyze: age estimates, gender estimation, pathologies, treatment recommendations, " +
                "forensic identity matching, or generate a complete summary of this evidence. " +
                "Please ask about any specific aspect of the dental analysis.", "Medium");
    }

    private string GenerateRulesBasedSummary(AnalysisContext context)
    {
        var summary = $"📋 FORENSIC DENTAL ANALYSIS SUMMARY\n\n";
        
        summary += $"🔍 DETECTION RESULTS:\n";
        summary += $"   • Teeth Detected: {context.TeethCount}\n";
        summary += $"   • Pathologies: {context.PathologiesCount}\n";
        summary += $"   • Processing Time: {context.ProcessingTimeMs:F0}ms\n\n";
        
        if (context.EstimatedAge.HasValue || !string.IsNullOrEmpty(context.EstimatedGender))
        {
            summary += $"👤 DEMOGRAPHIC ESTIMATION:\n";
            summary += $"   • Age: {context.EstimatedAge?.ToString() ?? "N/A"} years\n";
            summary += $"   • Gender: {context.EstimatedGender ?? "N/A"}\n\n";
        }
        
        if (context.DetectedPathologies.Count > 0)
        {
            summary += $"⚠️ PATHOLOGIES DETECTED:\n";
            foreach (var path in context.DetectedPathologies)
                summary += $"   • {path}\n";
            summary += "\n";
        }
        
        if (context.SmartInsights.Count > 0)
        {
            summary += $"💡 CLINSIGHTS:\n";
            foreach (var insight in context.SmartInsights.Take(5))
                summary += $"   • {insight}\n";
            summary += "\n";
        }
        
        if (context.UniquenessScore.HasValue)
        {
            summary += $"🔐 FORENSIC IDENTIFICATION:\n";
            summary += $"   • Uniqueness Score: {context.UniquenessScore:P0}\n";
            summary += $"   • Fingerprint Code: {context.FingerprintCode ?? "N/A"}\n";
        }
        
        return summary;
    }

    private List<TreatmentRecommendation> GenerateRulesBasedRecommendations(AnalysisContext context)
    {
        var recommendations = new List<TreatmentRecommendation>();
        
        foreach (var pathology in context.DetectedPathologies)
        {
            var rec = new TreatmentRecommendation();
            
            if (pathology.Contains("Caries"))
            {
                rec.Condition = "Dental Caries";
                rec.Treatment = "Composite Restoration / Excavation";
                rec.Priority = "Routine";
                rec.Prognosis = "Good";
                rec.Specialist = "General Dentist";
            }
            else if (pathology.Contains("Periapical") || pathology.Contains("Abscess"))
            {
                rec.Condition = "Periapical Pathology";
                rec.Treatment = "Root Canal Treatment + Crown";
                rec.Priority = "Urgent";
                rec.Prognosis = "Guarded";
                rec.Specialist = "Endodontist";
            }
            else if (pathology.Contains("Root Canal"))
            {
                rec.Condition = "Root Canal Treated Tooth";
                rec.Treatment = "Review RCT + Crown Restoration";
                rec.Priority = "Routine";
                rec.Prognosis = "Good";
                rec.Specialist = "Endodontist";
            }
            else if (pathology.Contains("Implant"))
            {
                rec.Condition = "Dental Implant";
                rec.Treatment = "Implant Maintenance Check";
                rec.Priority = "Routine";
                rec.Prognosis = "Good";
                rec.Specialist = "Prosthodontist";
            }
            else if (pathology.Contains("Crown"))
            {
                rec.Condition = "Dental Crown";
                rec.Treatment = "Crown Evaluation";
                rec.Priority = "Routine";
                rec.Prognosis = "Good";
            }
            else if (pathology.Contains("Root Piece"))
            {
                rec.Condition = "Root Remnant";
                rec.Treatment = "Surgical Extraction";
                rec.Priority = "Urgent";
                rec.Prognosis = "Good";
                rec.Specialist = "Oral Surgeon";
            }
            else if (pathology.Contains("Filling"))
            {
                rec.Condition = "Dental Restoration";
                rec.Treatment = "Restoration Check";
                rec.Priority = "Routine";
                rec.Prognosis = "Good";
            }
            
            if (!string.IsNullOrEmpty(rec.Condition))
                recommendations.Add(rec);
        }
        
        return recommendations;
    }

    private List<string> ExtractSources(AnalysisContext context)
    {
        var sources = new List<string>();
        
        if (context.TeethCount > 0)
            sources.Add($"Teeth Detection: {context.TeethCount} teeth");
        if (context.PathologiesCount > 0)
            sources.Add($"Pathology Detection: {context.PathologiesCount} findings");
        if (context.EstimatedAge.HasValue)
            sources.Add($"Age Estimation: {context.EstimatedAge}");
        if (context.EstimatedGender != null)
            sources.Add($"Gender Estimation: {context.EstimatedGender}");
            
        return sources;
    }

    private List<TreatmentRecommendation> ParseRecommendationsFromText(string text)
    {
        // Simple parsing for LLM responses
        var recommendations = new List<TreatmentRecommendation>();
        var lines = text.Split('\n');
        
        foreach (var line in lines)
        {
            if (line.Contains("Tooth") || line.Contains("#"))
            {
                recommendations.Add(new TreatmentRecommendation
                {
                    ToothNumber = "Various",
                    Condition = line,
                    Treatment = "See details",
                    Priority = "Routine"
                });
            }
        }
        
        return recommendations;
    }

    private enum LlmProvider
    {
        OpenAI,
        Anthropic,
        Local,
        RulesBased,
        Grok
    }

    // Response DTOs for LLM APIs
    private class OpenAiResponse
    {
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }

    private class OpenAiMessage
    {
        public string? Content { get; set; }
    }

    private class AnthropicResponse
    {
        public List<AnthropicContent>? Content { get; set; }
    }

    private class AnthropicContent
    {
        public string? Text { get; set; }
    }

    private class OllamaResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private class OllamaMessage
    {
        public string? Content { get; set; }
    }

    private class GrokResponse
    {
        public List<GrokChoice>? Choices { get; set; }
    }

    private class GrokChoice
    {
        public GrokMessage? Message { get; set; }
    }

    private class GrokMessage
    {
        public string? Content { get; set; }
    }
}

/// <summary>
/// Configuration interface for AI services.
/// </summary>
public interface IAiConfiguration
{
    string? LlmProvider { get; }
    string? LlmApiKey { get; }
    string? LlmModel { get; }
    string? LocalLlmEndpoint { get; }
    bool EnableRulesBasedFallback { get; }
}
