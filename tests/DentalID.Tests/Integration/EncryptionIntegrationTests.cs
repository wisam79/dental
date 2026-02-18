using Xunit;
using Microsoft.EntityFrameworkCore;
using DentalID.Infrastructure.Data;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using System;
using System.Linq;

namespace DentalID.Tests.Integration;

public class EncryptionIntegrationTests
{
    // Use a derived context to force EF Core to build a new model (and verify OnModelCreating runs with REAL encryption service)
    // instead of reusing the cached model from RepositoryTests which used a Mock pass-through service.
    private class TestAppDbContext : AppDbContext
    {
        public TestAppDbContext(DbContextOptions<AppDbContext> options, IEncryptionService encryptionService) 
            : base(options, encryptionService) { }
    }

    private readonly TestAppDbContext _context;
    private readonly EncryptionService _encryptionService;

    public EncryptionIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "EncryptionTestDb_" + Guid.NewGuid())
            .Options;

        var mockConfig = new Mock<IConfiguration>();
        _encryptionService = new EncryptionService(mockConfig.Object);

        // Use derived context
        _context = new TestAppDbContext(options, _encryptionService);
    }

    [Fact]
    public void SensitiveData_ShouldBeEncrypted_RoundTrip()
    {
        // 1. Save Data
        var subject = new Subject
        {
            SubjectId = "SUB-001",
            FullName = "John Doe Secret",
            CreatedAt = DateTime.UtcNow
        };

        _context.Subjects.Add(subject);
        _context.SaveChanges();

        // 2. Clear Context (Simulate new request)
        _context.ChangeTracker.Clear();

        // 3. Retrieve Data via Context (Should be decrypted transparently)
        var retrieved = _context.Subjects.First(s => s.SubjectId == "SUB-001");
        Assert.Equal("John Doe Secret", retrieved.FullName);
    }

    [Fact]
    public void RawData_ShouldBeDifferentFromPlaintext()
    {
        var entityType = _context.Model.FindEntityType(typeof(Subject));
        var property = entityType!.FindProperty(nameof(Subject.FullName));
        var converter = property!.GetValueConverter();

        Assert.NotNull(converter);
        // Verify it transforms data using the REAL service
        string plain = "Test";
        string? encrypted = converter.ConvertToProvider(plain) as string;
        
        Assert.NotNull(encrypted);
        Assert.NotEqual(plain, encrypted);
        Assert.True(encrypted!.Length > plain.Length); 
    }
}
