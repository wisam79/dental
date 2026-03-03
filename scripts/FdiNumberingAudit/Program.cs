using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FdiNumberingAudit;

internal static class Program
{
    private static readonly HashSet<int> ValidFdiNumbers = BuildValidFdiSet();

    private static int Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        string dbPath = options.DbPath ?? Path.Combine(Environment.CurrentDirectory, "dentalid.db");
        if (!File.Exists(dbPath))
        {
            Console.WriteLine($"[ERROR] Database not found: {dbPath}");
            return 1;
        }

        var records = LoadRecordsFromDatabase(dbPath, options.Take, options.MinConfidence);
        if (records.Count == 0)
        {
            Console.WriteLine("[WARN] No analysis records with teeth data were found.");
            return 0;
        }

        var perRecord = records.Select(ComputeMetrics).ToList();
        var summary = BuildSummary(perRecord);
        var modelInfo = ReadNumberingModelInfo(options.AppSettingsPath);

        PrintHeader();
        PrintModelInfo(modelInfo);
        PrintSummary(summary, perRecord.Count);

        if (!string.IsNullOrWhiteSpace(options.TruthCsvPath))
        {
            var truth = LoadGroundTruth(options.TruthCsvPath!);
            PrintTruthEvaluation(perRecord, truth);
        }

        if (!string.IsNullOrWhiteSpace(options.OutCsvPath))
        {
            WritePerRecordCsv(options.OutCsvPath!, perRecord);
            Console.WriteLine($"[OK] Per-record report written to: {options.OutCsvPath}");
        }

        return 0;
    }

    private static void PrintHeader()
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  DentalID FDI Numbering Capability Audit");
        Console.WriteLine("==============================================");
        Console.WriteLine("What this measures:");
        Console.WriteLine("- Coverage: how many unique valid FDI teeth are recovered.");
        Console.WriteLine("- Validity: how many detections fall in legal FDI ranges (11-48 pattern).");
        Console.WriteLine("- Duplicate load: repeated detections on the same FDI number.");
        Console.WriteLine("- Continuity: whether numbering follows expected tooth sequence per quadrant.");
        Console.WriteLine("- Balance: upper/lower arch distribution sanity.");
        Console.WriteLine();
    }

    private static void PrintModelInfo(NumberingModelInfo info)
    {
        Console.WriteLine("Numbering model profile:");
        Console.WriteLine($"- ClassMap length: {info.ClassMapLength}");
        Console.WriteLine($"- Mode: {info.Mode}");
        Console.WriteLine($"- Rule: {info.Rule}");
        Console.WriteLine();
    }

    private static void PrintSummary(Summary summary, int totalRecords)
    {
        Console.WriteLine("Dataset summary:");
        Console.WriteLine($"- Records analyzed: {totalRecords}");
        Console.WriteLine($"- Avg detected teeth: {summary.AverageDetected:F2}");
        Console.WriteLine($"- Avg unique valid FDI: {summary.AverageUniqueValid:F2}");
        Console.WriteLine($"- Avg coverage: {summary.AverageCoverage * 100:F1}% of 32");
        Console.WriteLine($"- Records >= 28 teeth: {summary.CoverageAtLeast28}/{totalRecords}");
        Console.WriteLine($"- Records >= 26 teeth: {summary.CoverageAtLeast26}/{totalRecords}");
        Console.WriteLine($"- FDI validity rate: {summary.ValidityRate * 100:F1}%");
        Console.WriteLine($"- Duplicate rate: {summary.DuplicateRate * 100:F1}%");
        Console.WriteLine($"- Avg continuity score: {summary.AverageContinuity * 100:F1}%");
        Console.WriteLine($"- Avg arch imbalance: {summary.AverageArchImbalance:F2}");
        Console.WriteLine($"- Capability score: {summary.CapabilityScore:F1}/100");
        Console.WriteLine();
    }

    private static void PrintTruthEvaluation(List<RecordMetrics> metrics, Dictionary<long, HashSet<int>> truth)
    {
        if (truth.Count == 0)
        {
            Console.WriteLine("[WARN] Ground truth file was provided but no valid rows were parsed.");
            Console.WriteLine();
            return;
        }

        long tp = 0;
        long fp = 0;
        long fn = 0;
        int matchedRecords = 0;

        foreach (var row in metrics)
        {
            if (!truth.TryGetValue(row.Id, out var expected))
                continue;

            matchedRecords++;
            var predicted = row.UniqueValidFdi;
            tp += predicted.Intersect(expected).LongCount();
            fp += predicted.Except(expected).LongCount();
            fn += expected.Except(predicted).LongCount();
        }

        if (matchedRecords == 0)
        {
            Console.WriteLine("[WARN] No record IDs matched between DB and truth CSV.");
            Console.WriteLine();
            return;
        }

        double precision = tp + fp > 0 ? tp / (double)(tp + fp) : 0;
        double recall = tp + fn > 0 ? tp / (double)(tp + fn) : 0;
        double f1 = precision + recall > 0 ? (2 * precision * recall) / (precision + recall) : 0;

        Console.WriteLine("Ground-truth evaluation (micro):");
        Console.WriteLine($"- Matched records: {matchedRecords}");
        Console.WriteLine($"- Precision: {precision * 100:F2}%");
        Console.WriteLine($"- Recall: {recall * 100:F2}%");
        Console.WriteLine($"- F1 score: {f1 * 100:F2}%");
        Console.WriteLine();
    }

    private static List<RecordInput> LoadRecordsFromDatabase(string dbPath, int take, double minConfidence)
    {
        var records = new List<RecordInput>();
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, AnalysisResults
FROM DentalImages
WHERE AnalysisResults IS NOT NULL
ORDER BY Id DESC
LIMIT $take;";
        command.Parameters.AddWithValue("$take", take);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            string analysisJson = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(analysisJson))
                continue;

            try
            {
                var detections = ParseTeethFromAnalysisJson(analysisJson, minConfidence);
                if (detections.Count == 0)
                    continue;

                records.Add(new RecordInput(id, detections));
            }
            catch
            {
                // Skip invalid records and continue audit.
            }
        }

        return records;
    }

    private static List<ToothDetection> ParseTeethFromAnalysisJson(string json, double minConfidence)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Teeth", out var teethElement) ||
            teethElement.ValueKind != JsonValueKind.Array)
        {
            return new List<ToothDetection>();
        }

        var teeth = new List<ToothDetection>();
        foreach (var item in teethElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!item.TryGetProperty("FdiNumber", out var fdiProp) || fdiProp.ValueKind != JsonValueKind.Number)
                continue;

            int fdi = fdiProp.GetInt32();
            float confidence = 0;
            if (item.TryGetProperty("Confidence", out var confProp) && confProp.ValueKind == JsonValueKind.Number)
            {
                confidence = confProp.GetSingle();
            }

            if (confidence < minConfidence)
                continue;

            teeth.Add(new ToothDetection(fdi, confidence));
        }

        return teeth;
    }

    private static RecordMetrics ComputeMetrics(RecordInput input)
    {
        var allFdi = input.Teeth.Select(t => t.FdiNumber).ToList();
        var validFdi = allFdi.Where(ValidFdiNumbers.Contains).ToList();
        var uniqueValid = validFdi.Distinct().ToHashSet();
        int duplicates = Math.Max(0, validFdi.Count - uniqueValid.Count);
        int invalid = allFdi.Count - validFdi.Count;
        double coverage = uniqueValid.Count / 32.0;

        int q1 = uniqueValid.Count(f => f >= 11 && f <= 18);
        int q2 = uniqueValid.Count(f => f >= 21 && f <= 28);
        int q3 = uniqueValid.Count(f => f >= 31 && f <= 38);
        int q4 = uniqueValid.Count(f => f >= 41 && f <= 48);
        int upper = q1 + q2;
        int lower = q3 + q4;
        int archImbalance = Math.Abs(upper - lower);

        double continuity = AverageContinuity(uniqueValid);
        double avgConfidence = input.Teeth.Count > 0 ? input.Teeth.Average(t => t.Confidence) : 0;

        return new RecordMetrics(
            input.Id,
            input.Teeth.Count,
            validFdi.Count,
            uniqueValid.Count,
            uniqueValid,
            duplicates,
            invalid,
            coverage,
            continuity,
            archImbalance,
            q1,
            q2,
            q3,
            q4,
            avgConfidence);
    }

    private static Summary BuildSummary(List<RecordMetrics> rows)
    {
        int totalDetected = rows.Sum(r => r.DetectedCount);
        int totalValid = rows.Sum(r => r.ValidCount);
        int totalDuplicates = rows.Sum(r => r.DuplicateCount);

        double avgCoverage = rows.Average(r => r.Coverage);
        double validityRate = totalDetected > 0 ? totalValid / (double)totalDetected : 0;
        double duplicateRate = totalValid > 0 ? totalDuplicates / (double)totalValid : 0;
        double avgContinuity = rows.Average(r => r.Continuity);
        double avgImbalance = rows.Average(r => r.ArchImbalance);
        int coverage26 = rows.Count(r => r.UniqueValidCount >= 26);
        int coverage28 = rows.Count(r => r.UniqueValidCount >= 28);

        double coverageComponent = avgCoverage * 45.0;
        double validityComponent = validityRate * 20.0;
        double duplicateComponent = (1.0 - Math.Clamp(duplicateRate, 0, 1)) * 10.0;
        double continuityComponent = avgContinuity * 15.0;
        double balanceComponent = (1.0 - Math.Clamp(avgImbalance / 16.0, 0, 1)) * 10.0;
        double capability = coverageComponent + validityComponent + duplicateComponent + continuityComponent + balanceComponent;

        return new Summary(
            rows.Average(r => r.DetectedCount),
            rows.Average(r => r.UniqueValidCount),
            avgCoverage,
            validityRate,
            duplicateRate,
            avgContinuity,
            avgImbalance,
            coverage26,
            coverage28,
            Math.Clamp(capability, 0, 100));
    }

    private static double AverageContinuity(HashSet<int> uniqueValidFdi)
    {
        var quadrants = new[]
        {
            uniqueValidFdi.Where(f => f >= 11 && f <= 18).Select(f => f % 10).Distinct().OrderBy(x => x).ToList(),
            uniqueValidFdi.Where(f => f >= 21 && f <= 28).Select(f => f % 10).Distinct().OrderBy(x => x).ToList(),
            uniqueValidFdi.Where(f => f >= 31 && f <= 38).Select(f => f % 10).Distinct().OrderBy(x => x).ToList(),
            uniqueValidFdi.Where(f => f >= 41 && f <= 48).Select(f => f % 10).Distinct().OrderBy(x => x).ToList()
        };

        double total = 0;
        int used = 0;

        foreach (var units in quadrants)
        {
            if (units.Count == 0)
                continue;

            used++;
            if (units.Count == 1)
            {
                total += 1;
                continue;
            }

            int adjacentPairs = 0;
            for (int i = 1; i < units.Count; i++)
            {
                if (units[i] - units[i - 1] == 1)
                    adjacentPairs++;
            }

            total += adjacentPairs / (double)(units.Count - 1);
        }

        return used > 0 ? total / used : 0;
    }

    private static NumberingModelInfo ReadNumberingModelInfo(string? appSettingsPath)
    {
        string path = appSettingsPath ?? Path.Combine(Environment.CurrentDirectory, "src", "DentalID.Desktop", "appsettings.json");
        if (!File.Exists(path))
        {
            return new NumberingModelInfo(0, "Unknown", "Could not locate appsettings.json");
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            int classMapLength = 0;
            if (doc.RootElement.TryGetProperty("AI", out var ai) &&
                ai.TryGetProperty("FdiMapping", out var fdi) &&
                fdi.TryGetProperty("ClassMap", out var map) &&
                map.ValueKind == JsonValueKind.Array)
            {
                classMapLength = map.GetArrayLength();
            }

            if (classMapLength >= 28)
            {
                return new NumberingModelInfo(
                    classMapLength,
                    "Direct class-to-FDI mapping (primary) + spatial fallback",
                    "Detector class index directly maps to FDI IDs; geometric refinement is secondary.");
            }

            return new NumberingModelInfo(
                classMapLength,
                "Spatial FDI refinement (primary)",
                "Class map is limited; numbering depends more on arch geometry and sequence heuristics.");
        }
        catch
        {
            return new NumberingModelInfo(0, "Unknown", "Failed to parse appsettings.json");
        }
    }

    private static Dictionary<long, HashSet<int>> LoadGroundTruth(string csvPath)
    {
        var truth = new Dictionary<long, HashSet<int>>();
        if (!File.Exists(csvPath))
            return truth;

        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',', 2);
            if (parts.Length < 2 || !long.TryParse(parts[0].Trim(), out var id))
                continue;

            var expected = parts[1]
                .Split(new[] { ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fdi) ? fdi : 0)
                .Where(ValidFdiNumbers.Contains)
                .ToHashSet();

            if (expected.Count > 0)
            {
                truth[id] = expected;
            }
        }

        return truth;
    }

    private static void WritePerRecordCsv(string outPath, List<RecordMetrics> rows)
    {
        using var writer = new StreamWriter(outPath);
        writer.WriteLine("Id,Detected,Valid,UniqueValid,MissingFrom32,Invalid,Duplicates,CoveragePct,ContinuityPct,ArchImbalance,Q1,Q2,Q3,Q4,AvgConfidence");

        foreach (var row in rows.OrderByDescending(r => r.Id))
        {
            writer.WriteLine(
                string.Join(",",
                    row.Id,
                    row.DetectedCount,
                    row.ValidCount,
                    row.UniqueValidCount,
                    32 - row.UniqueValidCount,
                    row.InvalidCount,
                    row.DuplicateCount,
                    (row.Coverage * 100).ToString("F2", CultureInfo.InvariantCulture),
                    (row.Continuity * 100).ToString("F2", CultureInfo.InvariantCulture),
                    row.ArchImbalance,
                    row.Q1,
                    row.Q2,
                    row.Q3,
                    row.Q4,
                    row.AverageConfidence.ToString("F4", CultureInfo.InvariantCulture)));
        }
    }

    private static HashSet<int> BuildValidFdiSet()
    {
        var valid = new HashSet<int>();
        foreach (var baseFdi in new[] { 10, 20, 30, 40 })
        {
            for (int unit = 1; unit <= 8; unit++)
            {
                valid.Add(baseFdi + unit);
            }
        }
        return valid;
    }

    private static AuditOptions ParseArgs(string[] args)
    {
        var opts = new AuditOptions
        {
            Take = 200,
            MinConfidence = 0
        };

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h")
            {
                opts.ShowHelp = true;
                return opts;
            }

            if (i + 1 >= args.Length)
                continue;

            switch (arg)
            {
                case "--db":
                    opts.DbPath = args[++i];
                    break;
                case "--take":
                    if (int.TryParse(args[++i], out int take) && take > 0)
                        opts.Take = take;
                    break;
                case "--min-confidence":
                    if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double conf))
                        opts.MinConfidence = Math.Clamp(conf, 0, 1);
                    break;
                case "--truth":
                    opts.TruthCsvPath = args[++i];
                    break;
                case "--out":
                    opts.OutCsvPath = args[++i];
                    break;
                case "--appsettings":
                    opts.AppSettingsPath = args[++i];
                    break;
            }
        }

        return opts;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project scripts/FdiNumberingAudit/FdiNumberingAudit.csproj -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --db <path>               Path to dentalid.db (default: ./dentalid.db)");
        Console.WriteLine("  --take <n>                Number of latest records to audit (default: 200)");
        Console.WriteLine("  --min-confidence <0..1>   Ignore teeth below confidence (default: 0)");
        Console.WriteLine("  --truth <csv>             Optional truth CSV: Id,ExpectedFdis (e.g. 123,11;12;13)");
        Console.WriteLine("  --out <csv>               Optional output CSV for per-record metrics");
        Console.WriteLine("  --appsettings <path>      Optional appsettings.json path");
        Console.WriteLine();
    }

    private sealed class AuditOptions
    {
        public string? DbPath { get; set; }
        public int Take { get; set; }
        public double MinConfidence { get; set; }
        public string? TruthCsvPath { get; set; }
        public string? OutCsvPath { get; set; }
        public string? AppSettingsPath { get; set; }
        public bool ShowHelp { get; set; }
    }

    private sealed record ToothDetection(int FdiNumber, float Confidence);
    private sealed record RecordInput(long Id, List<ToothDetection> Teeth);

    private sealed record RecordMetrics(
        long Id,
        int DetectedCount,
        int ValidCount,
        int UniqueValidCount,
        HashSet<int> UniqueValidFdi,
        int DuplicateCount,
        int InvalidCount,
        double Coverage,
        double Continuity,
        int ArchImbalance,
        int Q1,
        int Q2,
        int Q3,
        int Q4,
        double AverageConfidence);

    private sealed record Summary(
        double AverageDetected,
        double AverageUniqueValid,
        double AverageCoverage,
        double ValidityRate,
        double DuplicateRate,
        double AverageContinuity,
        double AverageArchImbalance,
        int CoverageAtLeast26,
        int CoverageAtLeast28,
        double CapabilityScore);

    private sealed record NumberingModelInfo(int ClassMapLength, string Mode, string Rule);
}
