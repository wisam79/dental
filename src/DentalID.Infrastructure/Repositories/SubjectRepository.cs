using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Repositories;

public class SubjectRepository : ISubjectRepository
{
    private readonly AppDbContext _db;

    public SubjectRepository(AppDbContext db) => _db = db;

    public async Task<Subject?> GetByIdAsync(int id)
        => await _db.Subjects
            .Include(s => s.DentalImages)
            .FirstOrDefaultAsync(s => s.Id == id);

    public async Task<Subject?> GetBySubjectIdAsync(string subjectId)
        => await _db.Subjects.FirstOrDefaultAsync(s => s.SubjectId == subjectId);

    public async Task<Subject?> GetByNationalIdAsync(string nationalId)
        => await _db.Subjects.FirstOrDefaultAsync(s => s.NationalId == nationalId);

    public async Task<List<Subject>> GetAllAsync(int page = 1, int pageSize = 20)
        => await _db.Subjects
            .Include(s => s.DentalImages)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

    public async Task<List<Subject>> GetAllWithVectorsAsync()
        => await _db.Subjects
            .Where(s => s.FeatureVector != null)
            .ToListAsync();

    public IAsyncEnumerable<Subject> StreamAllWithVectorsAsync()
        => _db.Subjects
            .AsNoTracking()
            .Where(s => s.FeatureVector != null)
            .AsAsyncEnumerable();

    public async Task<List<Subject>> SearchAsync(string query, int page = 1, int pageSize = 20)
    {
        // Optimization: ID search is indexed and unencrypted -> fast path
        if (query.StartsWith("SUB-", StringComparison.OrdinalIgnoreCase))
        {
             return await _db.Subjects
                .Include(s => s.DentalImages)
                .Where(s => s.SubjectId.StartsWith(query))
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        // Encryption limit: Partial name search requires client-side evaluation.
        // We fetch minimal projection to filter, then fetch full entities.
        // This is acceptable for < 10k records. For larger, we need Blind Indexing.
        
        var candidates = await _db.Subjects
            .AsNoTracking()
            .Select(s => new { s.Id, s.FullName, s.NationalId, s.CreatedAt })
            .ToListAsync(); // EF Core decrypts FullName here via ValueConverter

        var filteredIds = candidates
            .Where(s => (s.FullName != null && s.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (s.NationalId != null && s.NationalId.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => s.Id)
            .ToList();

        if (!filteredIds.Any()) return new List<Subject>();

        // Fetch full entities for the current page
        return await _db.Subjects
            .Include(s => s.DentalImages)
            .Where(s => filteredIds.Contains(s.Id))
            .OrderByDescending(s => s.CreatedAt) // Maintain order
            .ToListAsync();
    }

    public async Task<int> GetSearchCountAsync(string query)
    {
        // Optimization: ID search is indexed and unencrypted -> fast path
        if (query.StartsWith("SUB-", StringComparison.OrdinalIgnoreCase))
        {
             return await _db.Subjects
                .Where(s => s.SubjectId.StartsWith(query))
                .CountAsync();
        }

        // Encryption limit: Fetch minimal projection to count client-side.
        var candidates = await _db.Subjects
            .AsNoTracking()
            .Select(s => new { s.FullName, s.NationalId })
            .ToListAsync(); 

        return candidates
            .Count(s => (s.FullName != null && s.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (s.NationalId != null && s.NationalId.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<int> GetCountAsync()
        => await _db.Subjects.CountAsync();

    public async Task<Subject> AddAsync(Subject subject)
    {
        subject.CreatedAt = DateTime.UtcNow;
        subject.UpdatedAt = DateTime.UtcNow;
        subject.RowVersion = Guid.NewGuid().ToByteArray(); // Initialize Concurrency Token
        
        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync();
        return subject;
    }

    public async Task AddBatchAsync(IEnumerable<Subject> subjects)
    {
        foreach (var s in subjects) 
        {
            s.CreatedAt = DateTime.UtcNow;
            s.UpdatedAt = DateTime.UtcNow;
            s.RowVersion = Guid.NewGuid().ToByteArray();
        }
        await _db.Subjects.AddRangeAsync(subjects);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Subject subject)
    {
        subject.UpdatedAt = DateTime.UtcNow;
        
        // SQLite Concurrency: Manually rotate the token
        // Note: The caller must have preserved the Original RowVersion in the subject object.
        // EF Core 'Update' uses the property value as the concurrency check (OriginalValue).
        // specific handling might be needed if tracking behavior differs, but generally Update() sets State=Modified.
        // However, we need to change the RowVersion to a NEW value for the update, while checking the OLD one.
        
        // Ensure entity is tracked or attached first to manipulate values properly?
        // Simple approach: 
        // 1. Mark modified. 
        // 2. Set new RowVersion. 
        // 3. Ensure OriginalValue matches what was passed in.
        
        var entry = _db.Subjects.Update(subject);
        
        // Important: Update() sets the entity properties as "Current". 
        // We need to ensure the Concurrency Check uses the value passed in 'subject' before we change it to new value?
        // Actually, if we change it AFTER Update(), the ChangeTracker sees the change.
        
        var oldVersion = subject.RowVersion; 
        subject.RowVersion = Guid.NewGuid().ToByteArray(); // Rotate
        
        // If the entity was not tracked, Update() attached it. 
        // We must ensure the 'OriginalValue' for RowVersion is set to 'oldVersion' for the WHERE clause.
        entry.Property(p => p.RowVersion).OriginalValue = oldVersion;

        try 
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // For now, reload and throw, or just throw.
            // Upper layers (ViewModel) should handle re-fetching.
            throw; 
        }
    }

    public async Task DeleteAsync(int id)
    {
        var subject = await _db.Subjects.FindAsync(id);
        if (subject != null)
        {
            _db.Subjects.Remove(subject);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<string>> GetExistingSubjectIdsAsync(IEnumerable<string> subjectIds)
    {
        return await _db.Subjects
            .Where(s => subjectIds.Contains(s.SubjectId))
            .Select(s => s.SubjectId)
            .ToListAsync();
    }

    public async Task<Subject?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<Subject, bool>> predicate)
    {
        return await _db.Subjects.FirstOrDefaultAsync(predicate);
    }
}
