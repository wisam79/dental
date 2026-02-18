using System;
using System.IO;
using System.Threading.Tasks;
using DentalID.Application.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DentalID.Infrastructure.Services;

public class BackupService : IBackupService
{
    private readonly AppDbContext _db;
    private readonly ILogger<BackupService> _logger;

    public BackupService(AppDbContext db, ILogger<BackupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> CreateBackupAsync(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        var fileName = $"dentalid_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
        var backupPath = Path.Combine(backupDirectory, fileName);
        
        // Ensure path is absolute for SQLite
        backupPath = Path.GetFullPath(backupPath);

        _logger.LogInformation("Starting database backup to {BackupPath}", backupPath);

        try
        {
            // Bug #21 fix: Validate path to prevent SQL injection
            // VACUUM INTO does not support parameterized queries in SQLite
            if (backupPath.Contains('\'') || backupPath.Contains(';') || backupPath.Contains("--"))
            {
                throw new ArgumentException("Backup path contains invalid characters.", nameof(backupDirectory));
            }

            // SQLite specific: VACUUM INTO creates a transaction-consistent backup
            // even if the database is in use (Hot Backup).
            await _db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{backupPath}'");
            
            _logger.LogInformation("Database backup completed successfully.");
            return backupPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup failed.");
            throw new Exception($"Backup failed: {ex.Message}", ex);
        }
    }
}
