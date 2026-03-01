using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DentalID.Tests.Repositories;

public class SubjectRepositoryTests
{
    private static Mock<IEncryptionService> CreateEncryptionMock()
    {
        var mock = new Mock<IEncryptionService>();
        mock.Setup(x => x.Encrypt(It.IsAny<string>())).Returns((string s) => s);
        mock.Setup(x => x.Decrypt(It.IsAny<string>())).Returns((string s) => s);
        mock.Setup(x => x.ComputeDeterministicHash(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string value, string context) => $"{context}:{value}".ToUpperInvariant());
        return mock;
    }

    private static (AppDbContext Context, SubjectRepository Repository) CreateRepository()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var mockEncryption = CreateEncryptionMock();
        var context = new AppDbContext(options, mockEncryption.Object);
        var repository = new SubjectRepository(context, mockEncryption.Object);
        return (context, repository);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistSubject()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var subject = new Subject { FullName = "Test Subject", SubjectId = "SUB-001" };
        var result = await repo.AddAsync(subject);

        Assert.NotNull(result);
        Assert.NotEqual(0, result.Id);
        Assert.Equal("Test Subject", result.FullName);

        var saved = await context.Subjects.FindAsync(result.Id);
        Assert.NotNull(saved);
        Assert.Equal("SUB-001", saved!.SubjectId);
        Assert.NotNull(saved.FullNameLookupHash);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnSubject()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var subject = new Subject { FullName = "Test Subject 2", SubjectId = "SUB-002" };
        await repo.AddAsync(subject);

        var result = await repo.GetByIdAsync(subject.Id);

        Assert.NotNull(result);
        Assert.Equal(subject.Id, result!.Id);
    }

    [Fact]
    public async Task GetCountAsync_ShouldReturnCorrectCount()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.AddAsync(new Subject { FullName = "S1", SubjectId = "1" });
        await repo.AddAsync(new Subject { FullName = "S2", SubjectId = "2" });

        var count = await repo.GetCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifySubject()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var subject = new Subject { FullName = "Old Name", SubjectId = "SUB-003" };
        await repo.AddAsync(subject);

        subject.FullName = "New Name";
        await repo.UpdateAsync(subject);

        var updated = await repo.GetByIdAsync(subject.Id);
        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.FullName);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveSubject()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var subject = new Subject { FullName = "Delete Me", SubjectId = "SUB-004" };
        await repo.AddAsync(subject);

        await repo.DeleteAsync(subject.Id);

        var check = await repo.GetByIdAsync(subject.Id);
        Assert.Null(check);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnMatches()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.AddAsync(new Subject { FullName = "Alpha Beta", SubjectId = "A1" });
        await repo.AddAsync(new Subject { FullName = "Gamma Delta", SubjectId = "A2" });
        await repo.AddAsync(new Subject { FullName = "Alpha Omega", SubjectId = "A3" });

        var results = await repo.SearchAsync("Alpha");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, s => s.FullName == "Alpha Beta");
        Assert.Contains(results, s => s.FullName == "Alpha Omega");
    }

    [Fact]
    public async Task GetBySubjectIdAsync_ShouldReturnSubject()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.AddAsync(new Subject { FullName = "S", SubjectId = "ID-1" });

        var result = await repo.GetBySubjectIdAsync("ID-1");
        Assert.NotNull(result);
        Assert.Equal("S", result!.FullName);
    }

    [Fact]
    public async Task GetAllAsync_ShouldPaginate()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        for (int i = 0; i < 10; i++)
        {
            await repo.AddAsync(new Subject { FullName = $"S{i}", SubjectId = $"{i}" });
        }

        var page1 = await repo.GetAllAsync(1, 4);
        Assert.Equal(4, page1.Count);

        var page2 = await repo.GetAllAsync(2, 4);
        Assert.Equal(4, page2.Count);
        Assert.NotEqual(page1[0].Id, page2[0].Id);
    }

    [Fact]
    public async Task GetAllWithVectors_ShouldReturnSubjectsWithVectors()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.AddAsync(new Subject { FullName = "V1", FeatureVector = new byte[] { 1, 2 } });
        await repo.AddAsync(new Subject { FullName = "V2", FeatureVector = null });

        var list = await repo.GetAllWithVectorsAsync();
        Assert.Single(list);
        Assert.All(list, s => Assert.NotNull(s.FeatureVector));
    }

    [Fact]
    public async Task GetBySubjectIdAsync_ShouldReturnNull_WhenNotFound()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var result = await repo.GetBySubjectIdAsync("NON-EXISTENT");
        Assert.Null(result);
    }

    [Fact]
    public async Task AddBatchAsync_ShouldAddMultiple()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var batch = new List<Subject>
        {
            new() { FullName = "B1", SubjectId = "B1" },
            new() { FullName = "B2", SubjectId = "B2" }
        };

        await repo.AddBatchAsync(batch);
        Assert.Equal(2, await repo.GetCountAsync());
        Assert.All(batch, s => Assert.False(string.IsNullOrWhiteSpace(s.FullNameLookupHash)));
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ShouldNotThrow()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.DeleteAsync(999);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateTimestamps()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        var subject = await repo.AddAsync(new Subject { FullName = "Time", SubjectId = "TIME-1" });
        var originalTime = subject.UpdatedAt;

        await Task.Delay(10);
        subject.FullName = "Time2";
        await repo.UpdateAsync(subject);

        var updated = await repo.GetByIdAsync(subject.Id);
        Assert.NotNull(updated);
        Assert.True(updated!.UpdatedAt > originalTime);
        Assert.NotEqual(subject.FullNameLookupHash, updated.NationalIdLookupHash);
    }

    [Fact]
    public async Task SearchAsync_Pagination_ShouldWork()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        for (int i = 0; i < 5; i++)
        {
            await repo.AddAsync(new Subject { FullName = "Test", SubjectId = $"TEST-{i}" });
        }

        var results = await repo.SearchAsync("Test", 1, 2);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetByNationalIdAsync_ShouldMatchNormalizedValue()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.AddAsync(new Subject
        {
            FullName = "National Id Subject",
            SubjectId = "SUB-NAT-01",
            NationalId = "ab-12 34"
        });

        var found = await repo.GetByNationalIdAsync(" AB1234 ");

        Assert.NotNull(found);
        Assert.Equal("SUB-NAT-01", found!.SubjectId);
    }

    [Fact]
    public async Task GetByFullNameExactAsync_ShouldMatchNormalizedValue()
    {
        var setup = CreateRepository();
        using var context = setup.Context;
        var repo = setup.Repository;

        await repo.AddAsync(new Subject
        {
            FullName = "  Jane    Doe  ",
            SubjectId = "SUB-NAME-01"
        });

        var found = await repo.GetByFullNameExactAsync("jane doe");

        Assert.NotNull(found);
        Assert.Equal("SUB-NAME-01", found!.SubjectId);
    }
}
