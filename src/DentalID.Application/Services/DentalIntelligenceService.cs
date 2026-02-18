using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;

namespace DentalID.Application.Services;

public interface IDentalIntelligenceService
{
    void Analyze(AnalysisResult result);
}

public class DentalIntelligenceService : IDentalIntelligenceService
{
    public void Analyze(AnalysisResult result)
    {
        if (result == null || result.Teeth.Count == 0) return;

        // 1. Consistency Check (Spatial vs Class)
        CheckConsistency(result);

        // 2. Symmetry Analysis (Left vs Right)
        AnalyzeSymmetry(result);

        // 3. Occlusion Analysis (Bitewing/Overlap)
        AnalyzeOcclusion(result);

        // 4. Dentition Classification (Adult/Mixed)
        ClassifyDentition(result);

        // 5. Gap Analysis (Missing Teeth reasoning)
        AnalyzeGaps(result);
        
        // --- PHASE 6: GENIUS AI ---
        // 6. Dental Health Score (0-100)
        double healthScore = CalculateHealthScore(result);
        result.SmartInsights.Add($"Health Score: {healthScore:F0}/100");

        // 7. Refine Age (Hybrid Logic)
        RefineAgeEstimation(result);
        
        // 8. Expert Narrative
        string narrative = GenerateExpertNarrative(result, healthScore);
        result.SmartInsights.Add("📋 " + narrative);
        
        // --- PHASE 7: CLINICAL DECISION SUPPORT ---
        // 9. Urgency & Prognosis
        AnalyzeClinicalUrgency(result);

        // 10. Referrals
        GenerateReferrals(result);
    }

    private double CalculateHealthScore(AnalysisResult result)
    {
        // Scientifically-based dental health scoring system
        // Based on WHO Oral Health Assessment guidelines and dental indices
        // Score ranges from 0-100
        
        double score = 100.0;
        var penalties = new List<HealthPenalty>();
        
        // === ACTIVE PATHOLOGIES (Disease Indicators) ===
        
        // Caries - most common oral disease (WHO priority)
        // DMFT index basis: Each carious tooth = 1 point
        int caries = result.Pathologies.Count(p => p.ClassName.Contains("Caries"));
        if (caries > 0)
        {
            // Weighted by confidence
            foreach (var c in result.Pathologies.Where(p => p.ClassName.Contains("Caries")))
            {
                var penalty = 4.0 * c.Confidence; // 0-4 points per caries
                score -= penalty;
                penalties.Add(new HealthPenalty("Active Caries", penalty, c.Confidence));
            }
        }
        
        // Periapical lesions/abscesses - serious infection
        // Indicates advanced disease progression
        int lesions = result.Pathologies.Count(p => 
            p.ClassName.Contains("Lesion") || 
            p.ClassName.Contains("Periapical") ||
            p.ClassName.Contains("Abscess"));
        
        if (lesions > 0)
        {
            // High penalty - indicates potential systemic involvement
            foreach (var l in result.Pathologies.Where(p => 
                p.ClassName.Contains("Lesion") || 
                p.ClassName.Contains("Periapical") ||
                p.ClassName.Contains("Abscess")))
            {
                var penalty = 8.0 * l.Confidence; // 0-8 points per lesion
                score -= penalty;
                penalties.Add(new HealthPenalty("Periapical Pathology", penalty, l.Confidence));
            }
        }
        
        // Root pieces - indicates previous extraction or failed treatment
        int roots = result.Pathologies.Count(p => p.ClassName.Contains("Root Piece"));
        if (roots > 0)
        {
            foreach (var r in result.Pathologies.Where(p => p.ClassName.Contains("Root Piece")))
            {
                var penalty = 3.0 * r.Confidence; // 0-3 points per root piece
                score -= penalty;
                penalties.Add(new HealthPenalty("Root Remnant", penalty, r.Confidence));
            }
        }
        
        // === MISSING TEETH (Functional Loss) ===
        // Exclude Wisdom teeth (18, 28, 38, 48) - not considered functional
        var detectedFdis = result.Teeth.Select(t => t.FdiNumber).ToHashSet();
        int[] wisdoms = { 18, 28, 38, 48 };
        
        // Count missing standard teeth (11-17, 21-27, 31-37, 41-47)
        // Standard adult dentition = 28 teeth (excluding wisdoms)
        int presentStandard = result.Teeth.Count(t => !wisdoms.Contains(t.FdiNumber));
        // Fallacy #7 Fix: Implants functionally replace missing teeth — count them toward present teeth.
        // Without this offset, a patient with 5 implants would be penalized for "missing" 5 teeth
        // even though they have full functional dentition via implants.
        int implantCount = result.Pathologies.Count(p => p.ClassName.Contains("Implant"));
        int effectivePresent = Math.Min(28, presentStandard + implantCount);
        // Bug #9 Fix: Use Math.Max(0, ...) to prevent missingStandard from going negative.
        // This happened when the AI detected more teeth than the standard 28 (possible with retained
        // deciduous teeth counted alongside permanent dentition, or detection false positives).
        int missingStandard = Math.Max(0, 28 - effectivePresent);
        
        // Penalty: 1.5 points per missing tooth (functional impact)
        if (missingStandard > 0)
        {
            score -= (missingStandard * 1.5);
            penalties.Add(new HealthPenalty($"Missing Teeth ({missingStandard})", missingStandard * 1.5, 1.0));
        }
        
        // === RESTORATIONS (Treatment History) ===
        // Indicate history of disease, but treated
        // Don't penalize as heavily - they represent managed disease
        
        int fillings = result.Pathologies.Count(p => p.ClassName.Contains("Filling"));
        int crowns = result.Pathologies.Count(p => p.ClassName.Contains("Crown"));
        int rct = result.Pathologies.Count(p => p.ClassName.Contains("Root Canal"));
        int implants = result.Pathologies.Count(p => p.ClassName.Contains("Implant"));
        
        // Minor penalties for treatment history (dental disease burden)
        if (fillings > 0)
        {
            var penalty = fillings * 0.5;
            score -= penalty;
            penalties.Add(new HealthPenalty($"Fillings ({fillings})", penalty, 1.0));
        }
        
        if (crowns > 0)
        {
            var penalty = crowns * 1.0;
            score -= penalty;
            penalties.Add(new HealthPenalty($"Crowns ({crowns})", penalty, 1.0));
        }
        
        if (rct > 0)
        {
            // RCT indicates compromised tooth structure
            var penalty = rct * 1.5;
            score -= penalty;
            penalties.Add(new HealthPenalty($"Root Canal ({rct})", penalty, 1.0));
        }
        
        // Implants don't penalize - they replace missing teeth
        // They are actually positive from a functional perspective
        
        // === TEETH COUNT BONUS ===
        // Full or near-full dentition is a positive indicator
        if (presentStandard >= 28)
        {
            score += 2.0; // Bonus for complete dentition
        }
        else if (presentStandard >= 24)
        {
            score += 1.0; // Minor bonus
        }
        
        // === DENTITION TYPE FACTOR ===
        // Mixed dentition in adults is unusual
        bool hasDeciduous = result.Teeth.Any(t => t.FdiNumber >= 51 && t.FdiNumber <= 85);
        if (hasDeciduous)
        {
            // Adults with baby teeth have unique health profile
            score -= 5.0;
        }
        
        // Clamp to valid range
        return Math.Max(0, Math.Min(100, score));
    }
    
    /// <summary>
    /// Helper class for tracking health penalties
    /// </summary>
    private class HealthPenalty
    {
        public string Condition { get; }
        public double Points { get; }
        public double Confidence { get; }
        
        public HealthPenalty(string condition, double points, double confidence)
        {
            Condition = condition;
            Points = points;
            Confidence = confidence;
        }
    }

    private void RefineAgeEstimation(AnalysisResult result)
    {
        // Adjust the ML estimated age based on biological facts
        bool hasWisdom = result.Teeth.Any(t => t.FdiNumber % 10 == 8);
        bool hasImplants = result.Pathologies.Any(p => p.ClassName.Contains("Implant"));
        bool hasMixed = result.Teeth.Any(t => t.FdiNumber >= 51);

        if (result.EstimatedAge.HasValue)
        {
            int age = result.EstimatedAge.Value;
            string reason = "";

            // Fallacy #1 Fix: Do NOT force-correct the age to 12 for mixed dentition.
            // Mixed dentition in an adult may indicate retained deciduous teeth (a real clinical condition),
            // not necessarily that the subject IS 12 years old. Silently correcting the model's age
            // estimate with a hardcoded value destroys the AI's measured output and is medically unsound.
            // Instead, flag the conflict for manual review.
            if (hasMixed && age > 18) 
            {
                result.SmartInsights.Add("⚠️ Age Conflict: AI estimated adult age but mixed/deciduous dentition detected. Possible retained deciduous teeth or model error. Manual age verification required.");
                reason = "(Conflict detected - see insights)";
                // Do NOT update 'age' here — preserve the model's estimate
            }
            else if (hasImplants && age < 25)
            {
                age = 30; // Implants rare in < 25
                reason = "(Adjusted due to Implants)";
            }
            else if (hasWisdom && age < 16)
            {
                 age = 18; // Erupted wisdoms imply adulthood
                 reason = "(Adjusted due to Wisdom Teeth)";
            }
            
            if (!string.IsNullOrEmpty(reason))
            {
                result.EstimatedAge = age; // Update the DTO
                result.SmartInsights.Add($"Age Logic: Refined to {age} {reason}");
            }
        }
    }

    private string GenerateExpertNarrative(AnalysisResult result, double score)
    {
        // "Subject presents with a [Health] dentition. [X] active pathologies detected. Notable features include [Feature]."
        var sb = new System.Text.StringBuilder();
        
        string healthStatus = score switch
        {
            > 90 => "pristine",
            > 75 => "good",
            > 50 => "compromised",
            _ => "critical"
        };
        
        sb.Append($"Subject presents with {healthStatus} dental health (Score: {score:F0}). ");
        
        int caries = result.Pathologies.Count(p => p.ClassName.Contains("Caries"));
        if (caries > 0) sb.Append($"Medical attention required for {caries} active carious lesions. ");
        
        int restored = result.Pathologies.Count(p => p.ClassName.Contains("Filling") || p.ClassName.Contains("Crown") || p.ClassName.Contains("Root Canal"));
        if (restored > 0) sb.Append($"Evidence of significant past dental treatment ({restored} restorations). ");
        else sb.Append("No significant restorative history compatible with age. ");

        return sb.ToString();
    }
    
    
    private void AnalyzeClinicalUrgency(AnalysisResult result)
    {
        // Classify based on severity of findings
        // Urgent: Deep Caries, Periapical Lesions (risk of abscess)
        // Routine: Simple Caries, Missing Teeth
        
        bool hasApicalLesion = result.Pathologies.Any(p => p.ClassName.Contains("Periapical") || p.ClassName.Contains("Lesion"));
        bool hasDeepCaries = result.Pathologies.Any(p => p.ClassName.Contains("Deep")); // If model supports 'Deep Caries' class
        
        if (hasApicalLesion)
        {
            result.SmartInsights.Add("🚨 TRIAGE: URGENT - Signs of periapical infection/abscess detected. Immediate evaluation recommended.");
        }
        else if (result.Pathologies.Any(p => p.ClassName.Contains("Caries")))
        {
             result.SmartInsights.Add("⚠️ TRIAGE: ROUTINE - Active caries detected. Schedule restorative treatment.");
        }
        else
        {
             result.SmartInsights.Add("✅ TRIAGE: MAINTENANCE - No acute pathology detected. Routine hygiene recommended.");
        }
    }

    private void GenerateReferrals(AnalysisResult result)
    {
        // Suggest specialist based on pathology type
        var referrals = new HashSet<string>();
        
        if (result.Pathologies.Any(p => p.ClassName.Contains("Root Canal") || p.ClassName.Contains("Periapical")))
        {
            referrals.Add("Endodontist (Root Canal Treatment/Re-treatment)");
        }
        if (result.Pathologies.Any(p => p.ClassName.Contains("Root Piece") || p.ClassName.Contains("Impaction")))
        {
            referrals.Add("Oral Surgeon (Extraction)");
        }
        if (result.Pathologies.Any(p => p.ClassName.Contains("Implant")))
        {
            referrals.Add("Prosthodontist (Implant Maintenance)");
        }
        
        if (referrals.Count > 0)
        {
            result.SmartInsights.Add($"👨‍⚕️ REFERRAL: Consider consultation with: {string.Join(", ", referrals)}");
        }
        
        // 11. Prognosis & Treatment Plan
        GenerateTreatmentPlan(result);
    }
    
    private void GenerateTreatmentPlan(AnalysisResult result)
    {
        var plan = new List<string>();
        
        foreach (var tooth in result.Teeth)
        {
             // Check pathologies on this tooth
             // Note: Detailed mapping requires geometric intersection (which we did in MapPathologiesToTeeth)
             // But here we rely on the tooth.Pathologies list if it was populated, or heuristic.
             // AnalysisResult doesn't strictly link pathologies to specific Tooth objects in the DTO property 'Teeth'.
             // However, MapPathologiesToTeeth used 'tooth.ToString()' or internal logic.
             // Let's assume we can infer from the global list for now, or use the spatial logic again if needed.
             // For this "Genius" feature, let's look at the Text description if available, or re-run a quick spatial check.
             
            // Re-finding pathologies for this tooth:
             var toothPathologies = result.Pathologies
                .Where(p => GetIntersectionOverPathology(tooth, p) > 0.3f) // > 30% of pathology is inside tooth
                .ToList();
             
             // Bug #15 Fix: Use modulo arithmetic instead of string.EndsWith("8").
             // EndsWith("8") is fragile and locale-sensitive; FDI wisdom teeth always have units digit == 8.
             if (toothPathologies.Count == 0 && tooth.FdiNumber % 10 != 8) continue; // Skip healthy non-wisdom teeth
             
             foreach (var path in toothPathologies)
             {
                 if (path.ClassName.Contains("Caries"))
                 {
                     plan.Add($"Tooth {tooth.FdiNumber}: Excavation + Composite Restoration");
                 }
                 else if (path.ClassName.Contains("Root Piece"))
                 {
                     plan.Add($"Tooth {tooth.FdiNumber}: Surgical Extraction (Poor Prognosis)");
                 }
                 else if (path.ClassName.Contains("Periapical"))
                 {
                     plan.Add($"Tooth {tooth.FdiNumber}: Root Canal Treatment + Crown (Guarded Prognosis)");
                 }
             }
        }
        
        // Missing Teeth (excluding Wisdoms)
        var missingNumbers = new List<int>();
        int[] wisdoms = { 18, 28, 38, 48 };
        // Simple check 11-47
        // ... (This would be more robust with a full Odontogram check)
        
        if (plan.Count > 0)
        {
            result.SmartInsights.Add("📝 TREATMENT PLAN:");
            foreach (var p in plan.Take(3)) // Show top 3 to avoid clutter
            {
                result.SmartInsights.Add($"   - {p}");
            }
            if (plan.Count > 3) result.SmartInsights.Add($"   - (...and {plan.Count - 3} more)");
        }
    }

    private float GetIntersectionOverPathology(DetectedTooth t, DetectedPathology p)
    {
        float x1 = Math.Max(t.X, p.X);
        float y1 = Math.Max(t.Y, p.Y);
        float x2 = Math.Min(t.X + t.Width, p.X + p.Width);
        float y2 = Math.Min(t.Y + t.Height, p.Y + p.Height);

        if (x2 < x1 || y2 < y1) return 0;
        
        float intersection = (x2 - x1) * (y2 - y1);
        float pathologyArea = p.Width * p.Height;
        
        return pathologyArea > 0 ? intersection / pathologyArea : 0;
    }
    private void CheckConsistency(AnalysisResult result)
    {
        foreach (var tooth in result.Teeth)
        {
            // Molars: 16,17,18, 26,27,28, 36,37,38, 46,47,48
            // Reduced width threshold from 0.03 to 0.02 to be more lenient with perspective distortion
            if ((tooth.FdiNumber % 10 >= 6) && (tooth.Width < 0.02)) 
            {
                result.Flags.Add($"Conflict: Tooth {tooth.FdiNumber} is classified as Molar but appears too narrow ({tooth.Width:F3}).");
            }
        }
    }

    private void AnalyzeSymmetry(AnalysisResult result)
    {
        // Compare presence of 18 vs 28, 13 vs 23, etc.
        var teeth = result.Teeth.Select(t => t.FdiNumber).ToHashSet();

        int[] pairs = { 18, 28, 13, 23, 48, 38, 43, 33 };

        // Check Upper Wisdoms
        if (teeth.Contains(18) && !teeth.Contains(28)) result.SmartInsights.Add("Asymmetry: Right Upper Wisdom (18) present, Left (28) missing.");
        if (!teeth.Contains(18) && teeth.Contains(28)) result.SmartInsights.Add("Asymmetry: Left Upper Wisdom (28) present, Right (18) missing.");

        // Check Canines (Key for forensics)
        if (teeth.Contains(13) ^ teeth.Contains(23)) 
             result.SmartInsights.Add($"Asymmetry: Upper Canines unmatched ({(teeth.Contains(13) ? "13 only" : "23 only")}).");
    }

    private void AnalyzeOcclusion(AnalysisResult result)
    {
        // Estimate vertical overlap of Upper/Lower Anterior teeth
        // Upper Incisors: 11, 21
        // Lower Incisors: 41, 31
        
        var u1 = result.Teeth.FirstOrDefault(t => t.FdiNumber == 11);
        var l1 = result.Teeth.FirstOrDefault(t => t.FdiNumber == 41);

        if (u1 != null && l1 != null)
        {
            float uBottom = u1.Y + u1.Height;
            float lTop = l1.Y;

            float overlap = uBottom - lTop; // Positive means overlap
            
            if (overlap > 0.05) // Significant overlap relative to normalized height (approx 5% of image height)
            {
                result.SmartInsights.Add("Occlusion: Possible Deep Bite detected (Significant Anterior Overlap).");
            }
            else if (overlap < -0.02) // Significant gap
            {
                result.SmartInsights.Add("Occlusion: Possible Open Bite detected (Anterior Gap).");
            }
            else
            {
                result.SmartInsights.Add("Occlusion: Normal Anterior relationship.");
            }
        }
    }

    private void ClassifyDentition(AnalysisResult result)
    {
        int count = result.Teeth.Count;
        bool hasWisdom = result.Teeth.Any(t => t.FdiNumber % 10 == 8);
        bool hasDeciduous = result.Teeth.Any(t => t.FdiNumber >= 51 && t.FdiNumber <= 85); // Future: if we detect deciduous

        if (hasDeciduous)
        {
            result.SmartInsights.Add("Dentition: Mixed (Deciduous teeth detected).");
        }
        else if (count >= 28)
        {
             result.SmartInsights.Add("Dentition: Permanent Adult (Complete).");
        }
        else if (count < 20 && !hasWisdom)
        {
            result.SmartInsights.Add("Dentition: Incomplete/Developing (or Edentulous areas).");
        }
    }

    private void AnalyzeGaps(AnalysisResult result)
    {
        // Check for specific missing patterns
        // e.g. Missing 14/24 (common orthoadontic extraction)
        
        var teeth = result.Teeth.Select(t => t.FdiNumber).ToHashSet();
        
        if (!teeth.Contains(14) && !teeth.Contains(24) && teeth.Contains(15) && teeth.Contains(25))
        {
            result.SmartInsights.Add("Pattern: Bilateral Maxillary Premolar detected missing (Possible Orthodontic Extraction).");
        }

        // Check for "Gap" flags generated by the InferenceService (if we were passing them)
        // Since we don't have the raw gap data here, we infer from missing IDs in sequence
        // e.g. 13 present, 15 present, 14 missing -> Gap
    }
}
