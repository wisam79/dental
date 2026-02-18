using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DentalID.Tests.Repositories;

public class GenericRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IRepository<Subject> _repository;

    public GenericRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase")
            .Options;

        // Create mock encryption service
        var mockEncryptionService = new Mock<IEncryptionService>();
        mockEncryptionService.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => s); // No-op for testing
        mockEncryptionService.Setup(x => x.Decrypt(It.IsAny<string>())).Returns<string>(s => s); // No-op for testing

        _context = new AppDbContext(options, mockEncryptionService.Object);
        _repository = new GenericRepository<Subject>(_context);
        
        // Clear the database before each test
        _context.Subjects.RemoveRange(_context.Subjects);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task AddAsync_ShouldAddEntity_WhenValidEntity()
    {
        // Arrange
        var subject = new Subject
        {
            SubjectId = "SUB-TEST-001",
            FullName = "Test Subject",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        await _repository.AddAsync(subject);
        await _context.SaveChangesAsync();

        // Assert
        var addedSubject = await _context.Subjects.FindAsync(subject.Id);
        Assert.NotNull(addedSubject);
        Assert.Equal(subject.SubjectId, addedSubject.SubjectId);
        Assert.Equal(subject.FullName, addedSubject.FullName);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenEntityExists()
    {
        // Arrange
        var subject = new Subject
        {
            SubjectId = "SUB-TEST-002",
            FullName = "Another Test Subject",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(subject.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(subject.Id, result.Id);
        Assert.Equal(subject.SubjectId, result.SubjectId);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenEntityDoesNotExist()
    {
        // Arrange
        var nonExistentId = 999;

        // Act
        var result = await _repository.GetByIdAsync(nonExistentId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllEntities()
    {
        // Arrange
        var subjects = new List<Subject>
        {
            new Subject { SubjectId = "SUB-TEST-003", FullName = "Subject 1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Subject { SubjectId = "SUB-TEST-004", FullName = "Subject 2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Subject { SubjectId = "SUB-TEST-005", FullName = "Subject 3", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        _context.Subjects.AddRange(subjects);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(subjects.Count, result.Count);
        foreach (var subject in subjects)
        {
            Assert.Contains(result, s => s.SubjectId == subject.SubjectId);
        }
    }

    [Fact]
    public async Task Update_ShouldUpdateEntity_WhenEntityExists()
    {
        // Arrange
        var subject = new Subject
        {
            SubjectId = "SUB-TEST-006",
            FullName = "Original Name",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        subject.FullName = "Updated Name";
        _repository.Update(subject);
        await _context.SaveChangesAsync();

        // Assert
        var updatedSubject = await _context.Subjects.FindAsync(subject.Id);
        Assert.NotNull(updatedSubject);
        Assert.Equal("Updated Name", updatedSubject.FullName);
    }

    [Fact]
    public async Task Remove_ShouldDeleteEntity_WhenEntityExists()
    {
        // Arrange
        var subject = new Subject
        {
            SubjectId = "SUB-TEST-007",
            FullName = "Subject to Delete",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        _repository.Remove(subject);
        await _context.SaveChangesAsync();

        // Assert
        var deletedSubject = await _context.Subjects.FindAsync(subject.Id);
        Assert.Null(deletedSubject);
    }

    [Fact]
    public async Task AnyAsync_ShouldReturnTrue_WhenEntityExists()
    {
        // Arrange
        var subject = new Subject
        {
            SubjectId = "SUB-TEST-008",
            FullName = "Subject to Check",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.AnyAsync(s => s.SubjectId == "SUB-TEST-008");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AnyAsync_ShouldReturnFalse_WhenEntityDoesNotExist()
    {
        // Act
        var result = await _repository.AnyAsync(s => s.SubjectId == "SUB-NONEXISTENT");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var subjects = new List<Subject>
        {
            new Subject { SubjectId = "SUB-TEST-009", FullName = "Count Subject 1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Subject { SubjectId = "SUB-TEST-010", FullName = "Count Subject 2", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        _context.Subjects.AddRange(subjects);
        await _context.SaveChangesAsync();

        // Act
        var count = await _repository.CountAsync(s => s.SubjectId.StartsWith("SUB-TEST-"));

        // Assert
        Assert.Equal(subjects.Count, count);
    }
}
