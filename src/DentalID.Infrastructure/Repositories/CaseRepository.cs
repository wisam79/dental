using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Repositories;

public class CaseRepository : ICaseRepository
{
    private readonly AppDbContext _db;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 500;

    public CaseRepository(AppDbContext db) => _db = db;

    public async Task<Case?> GetByIdAsync(int id)
        => await _db.Cases
            .Include(c => c.Matches)
                .ThenInclude(m => m.MatchedSubject)
            .Include(c => c.AssignedTo)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Case?> GetByCaseNumberAsync(string caseNumber)
        => await _db.Cases.FirstOrDefaultAsync(c => c.CaseNumber == caseNumber);

    public async Task<List<Case>> GetAllAsync(int page = 1, int pageSize = 20)
    {
        (page, pageSize) = NormalizePaging(page, pageSize, DefaultPageSize);

        return await _db.Cases
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Case>> GetByStatusAsync(CaseStatus status)
        => await _db.Cases
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

    public async Task<List<Case>> GetActiveCasesAsync(int page = 1, int pageSize = 20)
    {
        (page, pageSize) = NormalizePaging(page, pageSize, DefaultPageSize);

        return await _db.Cases
            .Where(c => c.Status != CaseStatus.ClosedSolved && c.Status != CaseStatus.ClosedUnsolved)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetCountAsync(CaseStatus? status = null)
    {
        var query = _db.Cases.AsQueryable();
        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);
        return await query.CountAsync();
    }

    public async Task<Case> AddAsync(Case forensicCase)
    {
        if (forensicCase == null) throw new ArgumentNullException(nameof(forensicCase));

        bool hadCallerProvidedCaseNumber = !string.IsNullOrWhiteSpace(forensicCase.CaseNumber);
        const int maxAttempts = 5;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (string.IsNullOrWhiteSpace(forensicCase.CaseNumber))
            {
                forensicCase.CaseNumber = await GenerateCaseNumberAsync();
            }

            _db.Cases.Add(forensicCase);
            try
            {
                await _db.SaveChangesAsync();
                return forensicCase;
            }
            catch (DbUpdateException ex) when (IsCaseNumberUniqueConstraintViolation(ex))
            {
                _db.Entry(forensicCase).State = EntityState.Detached;

                // Keep caller-provided case numbers strict. Auto-generated numbers are retried.
                if (hadCallerProvidedCaseNumber || attempt == maxAttempts)
                    throw;

                forensicCase.CaseNumber = string.Empty;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique case number.");
    }

    public async Task UpdateAsync(Case forensicCase)
    {
        forensicCase.UpdatedAt = DateTime.UtcNow;
        _db.Cases.Update(forensicCase);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var c = await _db.Cases.FindAsync(id);
        if (c != null)
        {
            _db.Cases.Remove(c);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<string> GenerateCaseNumberAsync()
    {
        var todayPrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefixWithSeparator = todayPrefix + "-";

        var todayCaseNumbers = await _db.Cases
            .AsNoTracking()
            .Where(c => c.CaseNumber.StartsWith(prefixWithSeparator))
            .Select(c => c.CaseNumber)
            .ToListAsync();

        int nextSequence = todayCaseNumbers
            .Select(number => ParseCaseSequence(number, todayPrefix))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{todayPrefix}-{nextSequence:D3}";
    }

    private static int ParseCaseSequence(string? caseNumber, string expectedPrefix)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
            return 0;

        if (!caseNumber.StartsWith(expectedPrefix + "-", StringComparison.Ordinal))
            return 0;

        var sepIndex = caseNumber.LastIndexOf('-');
        if (sepIndex < 0 || sepIndex + 1 >= caseNumber.Length)
            return 0;

        return int.TryParse(caseNumber[(sepIndex + 1)..], out var value) && value > 0 ? value : 0;
    }

    private static bool IsCaseNumberUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("CaseNumber", StringComparison.OrdinalIgnoreCase);
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize, int defaultPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = defaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}
