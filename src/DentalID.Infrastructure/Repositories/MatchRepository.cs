using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Repositories;

public class MatchRepository : IMatchRepository
{
    private readonly AppDbContext _db;

    public MatchRepository(AppDbContext db) => _db = db;

    public async Task<Match?> GetByIdAsync(int id)
        => await _db.Matches
            .Include(m => m.QueryImage)
            .Include(m => m.MatchedSubject)
            .Include(m => m.Case)
            .FirstOrDefaultAsync(m => m.Id == id);

    public async Task<List<Match>> GetByCaseIdAsync(int caseId)
        => await _db.Matches
            .Include(m => m.QueryImage)
            .Include(m => m.MatchedSubject)
            .Where(m => m.CaseId == caseId)
            .OrderByDescending(m => m.ConfidenceScore)
            .ToListAsync();

    public async Task<List<Match>> GetBySubjectIdAsync(int subjectId)
        => await _db.Matches
            .Where(m => m.MatchedSubjectId == subjectId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

    public async Task<Match> AddAsync(Match match)
    {
        _db.Matches.Add(match);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        return match;
    }

    public async Task UpdateAsync(Match match)
    {
        _db.Matches.Update(match);
        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<List<Match>> GetRecentAsync(int count = 10)
        => await _db.Matches
            .Include(m => m.MatchedSubject)
            .OrderByDescending(m => m.CreatedAt)
            .Take(count)
            .ToListAsync();

    public async Task DeleteAsync(int id)
    {
        var match = await _db.Matches.FindAsync(id).ConfigureAwait(false);
        if (match != null)
        {
            _db.Matches.Remove(match);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
