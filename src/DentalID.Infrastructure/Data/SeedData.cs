using DentalID.Core.Entities;
using DentalID.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Data;

/// <summary>
/// Seeds the database with initial data on first run.
/// </summary>
public static class SeedData
{
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

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSchemaAsync(AppDbContext db)
    {
        // 1. Ensure Columns Exist (Manual Migration)
        try 
        {
            // Check if RowVersion exists. If not, add it.
            // SQLite specific pragma check
            var columns = await db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Subjects') WHERE name = 'RowVersion'").ToListAsync();
            if (!columns.Any())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Subjects ADD COLUMN RowVersion BLOB;");
            }

            // Check if MustChangePassword exists in Users. If not, add it.
            var userColumns = await db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Users') WHERE name = 'MustChangePassword'").ToListAsync();
            
            if (!userColumns.Any())
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;");
            }
        }
        catch (Exception ex)
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
            "CREATE INDEX IF NOT EXISTS IX_DentalImages_SubjectId ON DentalImages (SubjectId);" // FKs usually indexed, but ensure it.
        };

        foreach (var sql in indexes)
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }
}
