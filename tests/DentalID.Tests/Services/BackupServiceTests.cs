using System.IO;
using System.Threading.Tasks;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DentalID.Tests.Services;

public class BackupServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_ShouldCreateFile_AndContainData()
    {
        // Arrange: Use a real file-based DB for VACUUM INTO support
        var tempDir = Path.Combine(Path.GetTempPath(), "DentalID_BackupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "source.db");
        var backupDir = Path.Combine(tempDir, "backups");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .EnableSensitiveDataLogging()
                .Options;

            // Mock Encryption for Context
            var mockEncryption = new Mock<DentalID.Core.Interfaces.IEncryptionService>();
            mockEncryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => s);
            mockEncryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s);
            
            // 1. Setup Source DB
            using (var context = new AppDbContext(options, mockEncryption.Object))
            {
                try 
                {
                    await context.Database.EnsureCreatedAsync();
                    
                    // Bypass EF Core Insert to avoid RowVersion mapping issues in test environment
                    // SQLite BLOB literal for empty byte array is X''
                    // Insert minimal required fields
                    await context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO Subjects (SubjectId, FullName, CreatedAt, UpdatedAt, RowVersion) " +
                        "VALUES ('BKP-001', 'Backup Test Subject', date('now'), date('now'), randomblob(8))");
                }
                catch (Exception ex)
                {
                    throw new Exception($"TEST SETUP FAIL: {ex.Message}", ex);
                }
            }

            var mockLogger = new Mock<ILogger<BackupService>>();
            using (var context = new AppDbContext(options, mockEncryption.Object))
            {
                var service = new BackupService(context, mockLogger.Object);

                // Act
                var backupFile = await service.CreateBackupAsync(backupDir);

                // Assert
                Assert.True(File.Exists(backupFile), "Backup file should exist");
                Assert.True(new FileInfo(backupFile).Length > 0, "Backup file should not be empty");

                // Verify backup integrity by opening it
                var backupOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={backupFile}")
                    .Options;
                
                using var backupContext = new AppDbContext(backupOptions, mockEncryption.Object);
                var subject = await backupContext.Subjects.FirstOrDefaultAsync(s => s.SubjectId == "BKP-001");
                Assert.NotNull(subject);
                Assert.Equal("Backup Test Subject", subject.FullName);
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
