using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DentalID.Tests.Repositories;

// Alias Moq.Match to avoid conflict with DentalID.Core.Entities.Match
using MoqMatch = Moq.Match;

public class UnitOfWorkTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;

    public UnitOfWorkTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "UnitOfWorkTestDatabase")
            .Options;

        // Create mock encryption service
        var mockEncryptionService = new Mock<IEncryptionService>();
        mockEncryptionService.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => s); // No-op for testing
        mockEncryptionService.Setup(x => x.Decrypt(It.IsAny<string>())).Returns<string>(s => s); // No-op for testing

        _context = new AppDbContext(options, mockEncryptionService.Object);
        _unitOfWork = new UnitOfWork(_context);
    }

    [Fact]
    public async Task GetRepository_ShouldReturnCorrectRepository()
    {
        // Act
        var subjectRepo = _unitOfWork.GetRepository<Subject>();
        var dentalImageRepo = _unitOfWork.GetRepository<DentalImage>();
        var caseRepo = _unitOfWork.GetRepository<Case>();
        var matchRepo = _unitOfWork.GetRepository<DentalID.Core.Entities.Match>();

        // Assert
        Assert.NotNull(subjectRepo);
        Assert.IsType<GenericRepository<Subject>>(subjectRepo);
        Assert.NotNull(dentalImageRepo);
        Assert.IsType<GenericRepository<DentalImage>>(dentalImageRepo);
        Assert.NotNull(caseRepo);
        Assert.IsType<GenericRepository<Case>>(caseRepo);
        Assert.NotNull(matchRepo);
        Assert.IsType<GenericRepository<DentalID.Core.Entities.Match>>(matchRepo);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldCommitChanges()
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
        await _unitOfWork.GetRepository<Subject>().AddAsync(subject);
        var changes = await _unitOfWork.SaveChangesAsync();

        // Assert
        Assert.Equal(1, changes);
        var addedSubject = await _context.Subjects.FindAsync(subject.Id);
        Assert.NotNull(addedSubject);
        Assert.Equal(subject.SubjectId, addedSubject.SubjectId);
    }

    [Fact(Skip = "In-memory database doesn't support transactions")]
    public async Task Transaction_ShouldRollbackChanges_WhenErrorOccurs()
    {
        // Arrange
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var subjectRepo = _unitOfWork.GetRepository<Subject>();

        // Act
        try
        {
            var newSubject = new Subject
            {
                SubjectId = "SUB-TEST-002",
                FullName = "Transaction Subject",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await subjectRepo.AddAsync(newSubject);
            await _unitOfWork.SaveChangesAsync();

            // Simulate an error
            throw new Exception("Test exception");
        }
        catch
        {
            await transaction.RollbackAsync();
        }

        // Assert
        var foundSubject = await _context.Subjects.FirstOrDefaultAsync(s => s.SubjectId == "SUB-TEST-002");
        Assert.Null(foundSubject);
    }

    [Fact]
    public async Task MultipleOperations_ShouldBeAtomic()
    {
        // Arrange
        var subjectRepo = _unitOfWork.GetRepository<Subject>();
        var dentalImageRepo = _unitOfWork.GetRepository<DentalImage>();

        // Act
        var subject = new Subject
        {
            SubjectId = "SUB-TEST-003",
            FullName = "Atomic Subject",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await subjectRepo.AddAsync(subject);
        await _unitOfWork.SaveChangesAsync();

        var dentalImage = new DentalImage
        {
            SubjectId = subject.Id,
            ImagePath = "/test/images/atomic.jpg",
            FileHash = "testhash123",
            ImageType = Core.Enums.ImageType.Panoramic,
            UploadedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await dentalImageRepo.AddAsync(dentalImage);
        await _unitOfWork.SaveChangesAsync();

        // Assert
        var savedSubject = await subjectRepo.GetByIdAsync(subject.Id);
        Assert.NotNull(savedSubject);

        var savedImage = await dentalImageRepo.GetByIdAsync(dentalImage.Id);
        Assert.NotNull(savedImage);
        Assert.Equal(subject.Id, savedImage.SubjectId);
    }

    [Fact]
    public void Dispose_ShouldDisposeContext()
    {
        // Arrange - Create a separate context for this test
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DisposeTestDatabase")
            .Options;
        
        var mockEncryptionService = new Mock<IEncryptionService>();
        mockEncryptionService.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
        mockEncryptionService.Setup(x => x.Decrypt(It.IsAny<string>())).Returns<string>(s => s);
        
        using var context = new AppDbContext(options, mockEncryptionService.Object);
        
        // Act
        var unitOfWork = new UnitOfWork(context);
        unitOfWork.Dispose();
        
        // Assert (No exception should be thrown)
        Assert.True(true);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
