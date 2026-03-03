using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DentalID.Core.DTOs;
using DentalID.Application.Services;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DentalID.Tests.Services;

/// <summary>
/// Integration tests for <see cref="SearchService"/> using a real in-memory SQLite database.
/// Tests cover pagination, text filtering, sorting, and edge-case guard clauses.
/// </summary>
public class SearchServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly SearchService _service;
    private readonly Mock<IEncryptionService> _mockEncryption;

    public SearchServiceTests()
    {
        // Open a shared connection so InMemory SQLite survives across context creations
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _mockEncryption = new Mock<IEncryptionService>();
        _mockEncryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => s);
        _mockEncryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options, _mockEncryption.Object);
        _db.Database.EnsureCreated();

        // Use a factory-based UnitOfWork backed by our shared context
        var factory = new FixedContextFactory(_db);
        _uow = new UnitOfWork(factory);
        _service = new SearchService(_uow);
    }

    public void Dispose()
    {
        _uow.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }

    // ────── Helpers ──────────────────────────────────────────────────────────

    private async Task SeedSubjectsAsync(params Subject[] subjects)
    {
        foreach (var s in subjects)
        {
            await _db.Subjects.AddAsync(s);
        }
        await _db.SaveChangesAsync();
    }

    private static Subject MakeSubject(string id, string name, string? gender = null) =>
        new()
        {
            SubjectId = id,
            FullName = name,
            Gender = gender ?? "Unknown",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    // ────── Page guard-clause ────────────────────────────────────────────────

    [Fact]
    public async Task SearchSubjectsAsync_NegativePage_ShouldClampToOne()
    {
        await SeedSubjectsAsync(MakeSubject("S1", "Alice"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = -5, PageSize = 10 });

        result.Should().NotBeNull();
        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchSubjectsAsync_ZeroPageSize_ShouldClampTo20()
    {
        await SeedSubjectsAsync(MakeSubject("S1", "Alice"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 1, PageSize = 0 });

        result.PageSize.Should().Be(20);
        result.Items.Should().HaveCount(1);
    }

    // ────── Empty-query: returns all ─────────────────────────────────────────

    [Fact]
    public async Task SearchSubjectsAsync_EmptyQuery_ShouldReturnAll()
    {
        await SeedSubjectsAsync(
            MakeSubject("S1", "Alice"),
            MakeSubject("S2", "Bob"),
            MakeSubject("S3", "Carol"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 1, PageSize = 20 });

        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchSubjectsAsync_EmptyDb_ShouldReturnEmpty()
    {
        var result = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 1, PageSize = 20 });

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    // ────── Text filtering ───────────────────────────────────────────────────

    [Fact]
    public async Task SearchSubjectsAsync_FullNameFilter_ShouldMatch()
    {
        await SeedSubjectsAsync(
            MakeSubject("S1", "John Doe"),
            MakeSubject("S2", "Jane Smith"),
            MakeSubject("S3", "Bob Johnson"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto
        {
            SearchQuery = "john",
            Page = 1,
            PageSize = 20
        });

        result.TotalCount.Should().Be(2); // "John Doe" and "Bob Johnson"
        result.Items.Should().Contain(s => s.FullName == "John Doe");
        result.Items.Should().Contain(s => s.FullName == "Bob Johnson");
        result.Items.Should().NotContain(s => s.FullName == "Jane Smith");
    }

    [Fact]
    public async Task SearchSubjectsAsync_SubjectIdFilter_ShouldMatch()
    {
        await SeedSubjectsAsync(
            MakeSubject("FOR-001", "Ahmed"),
            MakeSubject("MED-002", "Sara"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto
        {
            SearchQuery = "FOR",
            Page = 1,
            PageSize = 20
        });

        result.TotalCount.Should().Be(1);
        result.Items[0].SubjectId.Should().Be("FOR-001");
    }

    [Fact]
    public async Task SearchSubjectsAsync_NoMatchingText_ShouldReturnEmpty()
    {
        await SeedSubjectsAsync(MakeSubject("S1", "Alice"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto
        {
            SearchQuery = "ZZZZZ_NO_MATCH",
            Page = 1,
            PageSize = 20
        });

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    // ────── Pagination ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSubjectsAsync_Pagination_ShouldRespectSkipTake()
    {
        // Seed 5 subjects
        await SeedSubjectsAsync(
            MakeSubject("S1", "Alice"),
            MakeSubject("S2", "Bob"),
            MakeSubject("S3", "Carol"),
            MakeSubject("S4", "Dave"),
            MakeSubject("S5", "Eve"));

        var page1 = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 1, PageSize = 2 });
        var page2 = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 2, PageSize = 2 });
        var page3 = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 3, PageSize = 2 });

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page3.Items.Should().HaveCount(1); // last page

        page1.TotalCount.Should().Be(5);
        page2.TotalCount.Should().Be(5);

        // Pages should be distinct
        var allNames = page1.Items.Concat(page2.Items).Concat(page3.Items)
            .Select(s => s.FullName)
            .ToList();
        allNames.Should().OnlyHaveUniqueItems();
    }

    // ────── Sorting ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSubjectsAsync_DefaultSort_ShouldOrderByName()
    {
        await SeedSubjectsAsync(
            MakeSubject("S1", "Zara"),
            MakeSubject("S2", "Alice"),
            MakeSubject("S3", "Mike"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto { Page = 1, PageSize = 10 });

        var names = result.Items.Select(s => s.FullName).ToList();
        names.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task SearchSubjectsAsync_SortById_ShouldOrderBySubjectId()
    {
        await SeedSubjectsAsync(
            MakeSubject("Z-999", "Zara"),
            MakeSubject("A-001", "Alice"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto
        {
            SortBy = "id",
            SortDescending = false,
            Page = 1,
            PageSize = 10
        });

        result.Items[0].SubjectId.Should().Be("A-001");
        result.Items[1].SubjectId.Should().Be("Z-999");
    }

    [Fact]
    public async Task SearchSubjectsAsync_SortDescending_ShouldReverseOrder()
    {
        await SeedSubjectsAsync(
            MakeSubject("S1", "Alice"),
            MakeSubject("S2", "Zara"),
            MakeSubject("S3", "Mike"));

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto
        {
            SortDescending = true,
            Page = 1,
            PageSize = 10
        });

        var names = result.Items.Select(s => s.FullName).ToList();
        names.Should().BeInDescendingOrder();
    }

    // ────── DTO Shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSubjectsAsync_ShouldPopulateDto_Correctly()
    {
        var dob = new DateTime(1990, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        await SeedSubjectsAsync(new Subject
        {
            SubjectId = "DTOTest",
            FullName = "Test Person",
            Gender = "Female",
            DateOfBirth = dob,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var result = await _service.SearchSubjectsAsync(new SearchParametersDto
        {
            SearchQuery = "DTOTest",
            Page = 1,
            PageSize = 10
        });

        result.Items.Should().HaveCount(1);
        var dto = result.Items[0];
        dto.SubjectId.Should().Be("DTOTest");
        dto.FullName.Should().Be("Test Person");
        dto.Gender.Should().Be("Female");
        dto.DateOfBirth.Should().Be(dob);
    }

    // ────── Fixed context factory ─────────────────────────────────────────────

    /// <summary>
    /// DbContextFactory that always returns the same pre-created DbContext instance,
    /// allowing the UnitOfWork to share the same connection used to seed test data.
    /// </summary>
    private sealed class FixedContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly AppDbContext _context;
        public FixedContextFactory(AppDbContext context) => _context = context;
        public AppDbContext CreateDbContext() => _context;
    }
}
