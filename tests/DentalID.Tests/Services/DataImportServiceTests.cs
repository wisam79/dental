using Xunit;
using DentalID.Infrastructure.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Linq;
using Moq;

namespace DentalID.Tests.Services;

public class DataImportServiceTests
{
    private class SubjectRepoStub : ISubjectRepository
    {
        public List<Subject> AddedSubjects { get; } = new();
        public List<Subject> ExistingSubjects { get; } = new();

        public Task<Subject> AddAsync(Subject subject)
        {
            if (subject.Id == 0)
            {
                subject.Id = AddedSubjects.Count + 1_000;
            }
            AddedSubjects.Add(subject);
            return Task.FromResult(subject);
        }

        public Task AddBatchAsync(IEnumerable<Subject> subjects)
        {
            AddedSubjects.AddRange(subjects);
            return Task.CompletedTask;
        }

        public Task<List<string>> GetExistingSubjectIdsAsync(IEnumerable<string> ids)
        {
            return Task.FromResult(new List<string>()); // No duplicates
        }

        // Unused methods
        public Task DeleteAsync(int id) => throw new NotImplementedException();
        public Task<List<Subject>> GetAllAsync(int page = 1, int pageSize = 20) => throw new NotImplementedException();
        public Task<List<Subject>> GetAllWithVectorsAsync() => throw new NotImplementedException();
        public Task<Subject?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<Subject?> GetBySubjectIdAsync(string subjectId) => throw new NotImplementedException();
        public Task<Subject?> GetByNationalIdAsync(string nationalId) => throw new NotImplementedException();
        public Task<Subject?> GetByFullNameExactAsync(string fullName)
        {
            var normalized = fullName?.Trim() ?? string.Empty;
            var existing = ExistingSubjects
                .Concat(AddedSubjects)
                .FirstOrDefault(s => string.Equals(s.FullName?.Trim(), normalized, System.StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(existing);
        }
        public Task<int> GetCountAsync() => throw new NotImplementedException();
        public Task<List<Subject>> SearchAsync(string query, int page = 1, int pageSize = 20) => throw new NotImplementedException();
        public Task<int> GetSearchCountAsync(string query) => throw new NotImplementedException();
        public Task<Subject?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<Subject, bool>> predicate) => throw new NotImplementedException();
        public IAsyncEnumerable<Subject> StreamAllWithVectorsAsync() => throw new NotImplementedException();
        public Task UpdateAsync(Subject subject) => throw new NotImplementedException();
    }

    [Fact]
    public async Task ImportSubjectsAsync_ShouldSkipEmptyNames()
    {
        // Arrange
        var repo = new SubjectRepoStub();
        var imageRepo = new Mock<IDentalImageRepository>();
        var service = new DataImportService(repo, imageRepo.Object);
        var subjects = new List<Subject>
        {
            new() { FullName = "Valid User", SubjectId = "1" },
            new() { FullName = "", SubjectId = "2" }, // Invalid
            new() { FullName = "   ", SubjectId = "3" } // Invalid
        };

        // Act
        var result = await service.ImportSubjectsAsync(subjects);

        // Assert
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(2, result.ErrorCount);
        Assert.Single(repo.AddedSubjects);
        Assert.Equal("Valid User", repo.AddedSubjects[0].FullName);
    }

    [Fact]
    public async Task ParseCsvAsync_ShouldHandleMissingColumnsGracefully()
    {
        // Arrange
        var repo = new SubjectRepoStub();
        var imageRepo = new Mock<IDentalImageRepository>();
        var service = new DataImportService(repo, imageRepo.Object);
        string csvData = @"Name,Age
User1,30
User2"; // User2 missing age
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvData));

        // Act
        var records = await service.ParseCsvAsync(stream);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("User1", records[0]["Name"]);
        Assert.Equal("User2", records[1]["Name"]);
        // CsvHelper might fill missing with null/empty depending on config, checking basic resilience
        Assert.True(records.Count > 0);
    }

    [Fact]
    public async Task ImportCaseFolderAsync_ShouldReuseExistingSubject_ByExactFullName()
    {
        var repo = new SubjectRepoStub();
        repo.ExistingSubjects.Add(new Subject
        {
            Id = 42,
            SubjectId = "SUB-EXIST-42",
            FullName = "John Doe"
        });

        var imageRepo = new Mock<IDentalImageRepository>();
        imageRepo.Setup(x => x.AddAsync(It.IsAny<DentalImage>()))
            .ReturnsAsync((DentalImage img) => img);

        var service = new DataImportService(repo, imageRepo.Object);

        var root = Path.Combine(Path.GetTempPath(), $"dental-import-{System.Guid.NewGuid():N}");
        var subjectDir = Path.Combine(root, "John Doe");
        Directory.CreateDirectory(subjectDir);
        var imagePath = Path.Combine(subjectDir, "img1.jpg");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 1, 2, 3, 4 });

        try
        {
            var result = await service.ImportCaseFolderAsync(root);

            Assert.Equal(1, result.SuccessCount);
            Assert.Empty(repo.AddedSubjects);
            imageRepo.Verify(x => x.AddAsync(It.Is<DentalImage>(d => d.SubjectId == 42)), Times.Once);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ImportCaseFolderAsync_ShouldNotCountFolderWithoutImagesAsSuccess()
    {
        var repo = new SubjectRepoStub();
        var imageRepo = new Mock<IDentalImageRepository>();
        var service = new DataImportService(repo, imageRepo.Object);

        var root = Path.Combine(Path.GetTempPath(), $"dental-import-empty-{System.Guid.NewGuid():N}");
        var subjectDir = Path.Combine(root, "NoImagesSubject");
        Directory.CreateDirectory(subjectDir);

        try
        {
            var result = await service.ImportCaseFolderAsync(root);

            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(1, result.ErrorCount);
            Assert.Contains(result.Errors, e => e.Contains("no supported images were imported", System.StringComparison.OrdinalIgnoreCase));
            imageRepo.Verify(x => x.AddAsync(It.IsAny<DentalImage>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
