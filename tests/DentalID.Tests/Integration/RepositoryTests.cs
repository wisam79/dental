using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DentalID.Core.Entities;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Moq;

namespace DentalID.Tests.Integration;

public class RepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly DentalID.Core.Interfaces.IEncryptionService _encryptionService;

    public RepositoryTests()
    {
        // Use SQLite in-memory mode
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open(); // Important: Keep connection open

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        var mockEncryption = new Moq.Mock<DentalID.Core.Interfaces.IEncryptionService>();
        mockEncryption.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => s); // Pass-through for repo tests
        mockEncryption.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s);
        mockEncryption.Setup(x => x.ComputeDeterministicHash(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string value, string context) => $"{context}:{value}".ToUpperInvariant());

        _encryptionService = mockEncryption.Object;
        _db = new AppDbContext(options, _encryptionService);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task StreamAllWithVectorsAsync_ShouldReturnOnlySubjectsWithVectors()
    {
        // Arrange
        var repo = new SubjectRepository(_db, _encryptionService);

        var s1 = new Subject { SubjectId = "SUB-001", FullName = "With Vector", FeatureVector = new byte[] { 1, 2, 3 } };
        var s2 = new Subject { SubjectId = "SUB-002", FullName = "No Vector", FeatureVector = null };
        var s3 = new Subject { SubjectId = "SUB-003", FullName = "With Vector 2", FeatureVector = new byte[] { 4, 5, 6 } };

        await repo.AddBatchAsync(new[] { s1, s2, s3 });

        // Act
        var results = new List<Subject>();
        await foreach (var s in repo.StreamAllWithVectorsAsync())
        {
            results.Add(s);
        }

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, s => s.FullName == "With Vector");
        Assert.Contains(results, s => s.FullName == "With Vector 2");
        Assert.DoesNotContain(results, s => s.FullName == "No Vector");
    }

    [Fact]
    public async Task StreamAllWithVectorsAsync_ShouldWorkWithEmptyDatabase()
    {
        // Arrange
        var repo = new SubjectRepository(_db, _encryptionService);

        // Act
        var results = new List<Subject>();
        await foreach (var s in repo.StreamAllWithVectorsAsync())
        {
            results.Add(s);
        }

        // Assert
        Assert.Empty(results);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
}
