using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Data;

/// <summary>
/// Seeds the database with initial data on first run.
/// </summary>
public static class SeedData
{
    private const string NationalIdHashContext = "subject:national-id:v1";
    private const string FullNameHashContext = "subject:full-name:v1";

    public static async Task InitializeAsync(AppDbContext db)
    {
        // Ensure database is created
        await db.Database.EnsureCreatedAsync();

        // Auth is disabled for personal use - No user seeding required.
        // if (!db.Users.Any()) { ... }

        // Seed ONNX model registry
        if (!db.AIModels.Any())
        {
            db.AIModels.AddRange(
                new AIModel
                {
                    Name = "Pathology Detection",
                    Type = "ObjectDetection",
                    Version = "1.0",
                    FilePath = "models/pathology_detect.onnx",
                    IsActive = true,
                    Parameters = "{\"inputSize\":640,\"classes\":[\"Caries\",\"Filling\",\"Crown\",\"Implant\",\"RootCanal\",\"Abscess\"]}"
                },
                new AIModel
                {
                    Name = "Teeth Detection",
                    Type = "ObjectDetection",
                    Version = "1.0",
                    FilePath = "models/teeth_detect.onnx",
                    IsActive = true,
                    Parameters = "{\"inputSize\":640,\"system\":\"FDI\"}"
                },
                new AIModel
                {
                    Name = "Feature Encoder",
                    Type = "FeatureExtraction",
                    Version = "1.0",
                    FilePath = "models/encoder.onnx",
                    IsActive = true,
                    Parameters = "{\"inputSize\":224,\"outputDim\":128}"
                },
                new AIModel
                {
                    Name = "Feature Decoder",
                    Type = "FeatureExtraction",
                    Version = "1.0",
                    FilePath = "models/decoder.onnx",
                    IsActive = true
                },
                new AIModel
                {
                    Name = "Age/Gender Estimation",
                    Type = "Classification",
                    Version = "1.0",
                    FilePath = "models/genderage.onnx",
                    IsActive = true,
                    Parameters = "{\"inputSize\":96}"
                },
                new AIModel
                {
                    Name = "Matching Pipeline",
                    Type = "Matching",
                    Version = "1.0",
                    FilePath = "models/matching_pipeline.onnx",
                    IsActive = true
                }
            );
        }

        // Apply Schema Updates manually since we use EnsureCreated (which doesn't handle migrations for existing DBs)
        await EnsureSchemaAsync(db);
        var updatedRows = await BackfillSubjectLookupHashesAsync(db);
        Console.WriteLine($"[Schema] Subject lookup hash backfill completed. Updated rows: {updatedRows}");

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSchemaAsync(AppDbContext db)
    {
        // 1. Ensure Columns Exist (Manual Migration)
        try
        {
            await EnsureColumnExistsAsync(db, "Subjects", "RowVersion", "ALTER TABLE Subjects ADD COLUMN RowVersion BLOB;");
            await EnsureColumnExistsAsync(db, "Subjects", "NationalIdLookupHash", "ALTER TABLE Subjects ADD COLUMN NationalIdLookupHash TEXT NULL;");
            await EnsureColumnExistsAsync(db, "Subjects", "FullNameLookupHash", "ALTER TABLE Subjects ADD COLUMN FullNameLookupHash TEXT NULL;");

            // Keep auth schema backward compatible while runtime auth remains dormant.
            await EnsureColumnExistsAsync(db, "Users", "MustChangePassword", "ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;");
            await EnsureColumnExistsAsync(db, "Users", "FailedLoginAttempts", "ALTER TABLE Users ADD COLUMN FailedLoginAttempts INTEGER NOT NULL DEFAULT 0;");
            await EnsureColumnExistsAsync(db, "Users", "LockedUntil", "ALTER TABLE Users ADD COLUMN LockedUntil TEXT NULL;");
        }
        catch (Exception)
        {
             // Log or ignore if column already exists (race condition or weird state)
             // Console.WriteLine($"Schema Update Warning: {ex.Message}");
        }

        // 2. Ensure Indexes Exist
        // SQLite syntax for creating indexes if they don't exist
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS IX_Subjects_CreatedAt ON Subjects (CreatedAt);",
            "CREATE INDEX IF NOT EXISTS IX_DentalImages_CreatedAt ON DentalImages (CreatedAt);",
            "CREATE INDEX IF NOT EXISTS IX_Cases_CreatedAt ON Cases (CreatedAt);",
            "CREATE INDEX IF NOT EXISTS IX_Matches_CreatedAt ON Matches (CreatedAt);",
            "CREATE INDEX IF NOT EXISTS IX_AuditLog_Timestamp ON AuditLog (Timestamp);",
            "CREATE INDEX IF NOT EXISTS IX_DentalImages_SubjectId ON DentalImages (SubjectId);", // FKs usually indexed, but ensure it.
            "CREATE INDEX IF NOT EXISTS IX_Subjects_NationalIdLookupHash ON Subjects (NationalIdLookupHash);",
            "CREATE INDEX IF NOT EXISTS IX_Subjects_FullNameLookupHash ON Subjects (FullNameLookupHash);"
        };

        foreach (var sql in indexes)
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    private static async Task EnsureColumnExistsAsync(AppDbContext db, string tableName, string columnName, string alterSql)
    {
        var sql = $"SELECT name FROM pragma_table_info('{tableName}') WHERE name = '{columnName}'";
        var columns = await db.Database.SqlQueryRaw<string>(sql).ToListAsync();
        if (!columns.Any())
        {
            await db.Database.ExecuteSqlRawAsync(alterSql);
        }
    }

    private static async Task<int> BackfillSubjectLookupHashesAsync(AppDbContext db, int batchSize = 200)
    {
        var updatedRows = 0;
        var scannedRows = 0;
        var lastProcessedId = 0;

        while (true)
        {
            var subjects = await db.Subjects
                .Where(s => s.Id > lastProcessedId &&
                            (s.NationalIdLookupHash == null || s.FullNameLookupHash == null))
                .OrderBy(s => s.Id)
                .Take(batchSize)
                .ToListAsync();

            if (subjects.Count == 0)
            {
                break;
            }

            lastProcessedId = subjects[^1].Id;
            var changedInBatch = 0;

            foreach (var subject in subjects)
            {
                var expectedNationalIdHash = BuildLookupHash(
                    db,
                    SubjectRepository.NormalizeNationalId(subject.NationalId),
                    NationalIdHashContext);

                var expectedFullNameHash = BuildLookupHash(
                    db,
                    SubjectRepository.NormalizeFullName(subject.FullName),
                    FullNameHashContext);

                var changed = false;
                if (!string.Equals(subject.NationalIdLookupHash, expectedNationalIdHash, StringComparison.Ordinal))
                {
                    subject.NationalIdLookupHash = expectedNationalIdHash;
                    changed = true;
                }

                if (!string.Equals(subject.FullNameLookupHash, expectedFullNameHash, StringComparison.Ordinal))
                {
                    subject.FullNameLookupHash = expectedFullNameHash;
                    changed = true;
                }

                if (changed)
                {
                    subject.UpdatedAt = DateTime.UtcNow;
                    changedInBatch++;
                }
            }

            if (changedInBatch > 0)
            {
                await db.SaveChangesAsync();
                updatedRows += changedInBatch;
            }

            scannedRows += subjects.Count;
            Console.WriteLine($"[Schema] Backfill progress: scanned={scannedRows}, updated={updatedRows}, lastId={lastProcessedId}");
            db.ChangeTracker.Clear();
        }

        return updatedRows;
    }

    private static string? BuildLookupHash(AppDbContext db, string? normalizedValue, string context)
    {
        if (string.IsNullOrEmpty(normalizedValue))
        {
            return null;
        }

        return db.ComputeDeterministicHash(normalizedValue, context);
    }
}
