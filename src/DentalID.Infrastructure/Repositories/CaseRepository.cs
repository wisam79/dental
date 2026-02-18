using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Repositories;

public class CaseRepository : ICaseRepository
{
    private readonly AppDbContext _db;

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
        => await _db.Cases
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

    public async Task<List<Case>> GetByStatusAsync(CaseStatus status)
        => await _db.Cases
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

    public async Task<List<Case>> GetActiveCasesAsync(int page = 1, int pageSize = 20)
        => await _db.Cases
            .Where(c => c.Status != CaseStatus.ClosedSolved && c.Status != CaseStatus.ClosedUnsolved)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

    public async Task<int> GetCountAsync(CaseStatus? status = null)
    {
        var query = _db.Cases.AsQueryable();
        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);
        return await query.CountAsync();
    }

    public async Task<Case> AddAsync(Case forensicCase)
    {
        _db.Cases.Add(forensicCase);
        await _db.SaveChangesAsync();
        return forensicCase;
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
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var todayCount = await _db.Cases
            .CountAsync(c => c.CaseNumber.StartsWith(today));
        return $"{today}-{(todayCount + 1):D3}";
    }
}
