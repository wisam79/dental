using Xunit;
using DentalID.Infrastructure.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Moq;

namespace DentalID.Tests.Services;

public class DataImportServiceTests
{
    private class SubjectRepoStub : ISubjectRepository
    {
        public List<Subject> AddedSubjects { get; } = new();

        public Task<Subject> AddAsync(Subject subject)
        {
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
}
