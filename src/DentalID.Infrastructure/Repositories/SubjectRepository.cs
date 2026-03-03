using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DentalID.Infrastructure.Repositories;

public class SubjectRepository : ISubjectRepository
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _encryptionService;
    private const string NationalIdHashContext = "subject:national-id:v1";
    private const string FullNameHashContext = "subject:full-name:v1";
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 500;

    public SubjectRepository(AppDbContext db, IEncryptionService encryptionService)
    {
        _db = db;
        _encryptionService = encryptionService;
    }

    public async Task<Subject?> GetByIdAsync(int id)
        => await _db.Subjects
            .Include(s => s.DentalImages)
            .FirstOrDefaultAsync(s => s.Id == id);

    public async Task<Subject?> GetBySubjectIdAsync(string subjectId)
        => await _db.Subjects
            .Include(s => s.DentalImages)
            .FirstOrDefaultAsync(s => s.SubjectId == subjectId);

    public async Task<Subject?> GetByNationalIdAsync(string nationalId)
    {
        var normalizedNationalId = NormalizeNationalId(nationalId);
        if (string.IsNullOrEmpty(normalizedNationalId))
        {
            return null;
        }

        var lookupHash = _encryptionService.ComputeDeterministicHash(normalizedNationalId, NationalIdHashContext);

        var hashCandidates = await _db.Subjects
            .Where(s => s.NationalIdLookupHash == lookupHash)
            .ToListAsync();

        var verified = hashCandidates.FirstOrDefault(s =>
            string.Equals(NormalizeNationalId(s.NationalId), normalizedNationalId, StringComparison.Ordinal));

        if (verified != null)
        {
            return verified;
        }

        // Legacy fallback for rows created before lookup hash backfill.
        var legacyCandidates = await _db.Subjects
            .Where(s => s.NationalIdLookupHash == null && s.NationalId != null)
            .ToListAsync();

        return legacyCandidates.FirstOrDefault(s =>
            string.Equals(NormalizeNationalId(s.NationalId), normalizedNationalId, StringComparison.Ordinal));
    }

    public async Task<Subject?> GetByFullNameExactAsync(string fullName)
    {
        var normalizedFullName = NormalizeFullName(fullName);
        if (string.IsNullOrEmpty(normalizedFullName))
        {
            return null;
        }

        var lookupHash = _encryptionService.ComputeDeterministicHash(normalizedFullName, FullNameHashContext);

        var hashCandidates = await _db.Subjects
            .Where(s => s.FullNameLookupHash == lookupHash)
            .ToListAsync();

        var verified = hashCandidates.FirstOrDefault(s =>
            string.Equals(NormalizeFullName(s.FullName), normalizedFullName, StringComparison.Ordinal));

        if (verified != null)
        {
            return verified;
        }

        // Legacy fallback for rows created before lookup hash backfill.
        var legacyCandidates = await _db.Subjects
            .Where(s => s.FullNameLookupHash == null)
            .ToListAsync();

        return legacyCandidates.FirstOrDefault(s =>
            string.Equals(NormalizeFullName(s.FullName), normalizedFullName, StringComparison.Ordinal));
    }

    public async Task<List<Subject>> GetAllAsync(int page = 1, int pageSize = 20)
    {
        (page, pageSize) = NormalizePaging(page, pageSize, DefaultPageSize);

        return await _db.Subjects
            .Include(s => s.DentalImages)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Subject>> GetAllWithVectorsAsync()
        => await _db.Subjects
            .Where(s => s.FeatureVector != null)
            .ToListAsync();

    public IAsyncEnumerable<Subject> StreamAllWithVectorsAsync()
        => _db.Subjects
            .AsNoTracking()
            .Include(s => s.DentalImages)
            .Where(s => s.FeatureVector != null)
            .AsAsyncEnumerable();

    public async Task<List<Subject>> SearchAsync(string query, int page = 1, int pageSize = 20)
    {
        query = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync(page, pageSize);

        (page, pageSize) = NormalizePaging(page, pageSize, DefaultPageSize);

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

        // Use deterministic hash for exact match instead of client-side partial search
        var normalizedName = NormalizeFullName(query);
        var nameHash = normalizedName != null ? _encryptionService.ComputeDeterministicHash(normalizedName, FullNameHashContext) : "INVALID_HASH";

        var normalizedId = NormalizeNationalId(query);
        var idHash = normalizedId != null ? _encryptionService.ComputeDeterministicHash(normalizedId, NationalIdHashContext) : "INVALID_HASH";

        return await _db.Subjects
            .Include(s => s.DentalImages)
            .Where(s => s.FullNameLookupHash == nameHash || s.NationalIdLookupHash == idHash)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetSearchCountAsync(string query)
    {
        query = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            return await GetCountAsync().ConfigureAwait(false);

        // Optimization: ID search is indexed and unencrypted -> fast path
        if (query.StartsWith("SUB-", StringComparison.OrdinalIgnoreCase))
        {
             return await _db.Subjects
                .Where(s => s.SubjectId.StartsWith(query))
                .CountAsync();
        }

        // Use deterministic hash for exact match
        var normalizedName = NormalizeFullName(query);
        var nameHash = normalizedName != null ? _encryptionService.ComputeDeterministicHash(normalizedName, FullNameHashContext) : "INVALID_HASH";

        var normalizedId = NormalizeNationalId(query);
        var idHash = normalizedId != null ? _encryptionService.ComputeDeterministicHash(normalizedId, NationalIdHashContext) : "INVALID_HASH";

        return await _db.Subjects
            .Where(s => s.FullNameLookupHash == nameHash || s.NationalIdLookupHash == idHash)
            .CountAsync();
    }

    public async Task<int> GetCountAsync()
        => await _db.Subjects.CountAsync().ConfigureAwait(false);

    public async Task<Subject> AddAsync(Subject subject)
    {
        subject.CreatedAt = DateTime.UtcNow;
        subject.UpdatedAt = DateTime.UtcNow;
        subject.RowVersion = Guid.NewGuid().ToByteArray(); // Initialize Concurrency Token
        PopulateLookupHashes(subject);
        
        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync().ConfigureAwait(false);
        return subject;
    }

    public async Task AddBatchAsync(IEnumerable<Subject> subjects)
    {
        foreach (var s in subjects) 
        {
            s.CreatedAt = DateTime.UtcNow;
            s.UpdatedAt = DateTime.UtcNow;
            s.RowVersion = Guid.NewGuid().ToByteArray();
            PopulateLookupHashes(s);
        }
        await _db.Subjects.AddRangeAsync(subjects).ConfigureAwait(false);
        await _db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task UpdateAsync(Subject subject)
    {
        subject.UpdatedAt = DateTime.UtcNow;
        PopulateLookupHashes(subject);
        
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
            await _db.SaveChangesAsync().ConfigureAwait(false);
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
        var subject = await _db.Subjects
            .Include(s => s.DentalImages)
            .FirstOrDefaultAsync(s => s.Id == id);
            
        if (subject != null)
        {
            var imagesToDelete = subject.DentalImages
                .Select(img => img.ImagePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
                
            _db.Subjects.Remove(subject);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            // Bug #15 fix: Prevent orphaned physical files when subject is deleted
            foreach (var imgPath in imagesToDelete)
            {
                if (System.IO.File.Exists(imgPath))
                {
                    try { System.IO.File.Delete(imgPath); } catch { }
                }
            }
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
        return await _db.Subjects.FirstOrDefaultAsync(predicate).ConfigureAwait(false);
    }

    private void PopulateLookupHashes(Subject subject)
    {
        var normalizedNationalId = NormalizeNationalId(subject.NationalId);
        subject.NationalIdLookupHash = string.IsNullOrEmpty(normalizedNationalId)
            ? null
            : _encryptionService.ComputeDeterministicHash(normalizedNationalId, NationalIdHashContext);

        var normalizedFullName = NormalizeFullName(subject.FullName);
        subject.FullNameLookupHash = string.IsNullOrEmpty(normalizedFullName)
            ? null
            : _encryptionService.ComputeDeterministicHash(normalizedFullName, FullNameHashContext);
    }

    internal static string? NormalizeNationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
        {
            return null;
        }

        var trimmed = nationalId.Trim();
        var buffer = new StringBuilder(trimmed.Length);

        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                continue;
            }

            buffer.Append(char.ToUpperInvariant(ch));
        }

        return buffer.Length == 0 ? null : buffer.ToString();
    }

    internal static string? NormalizeFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var trimmed = fullName.Trim();
        var buffer = new StringBuilder(trimmed.Length);
        var previousWasSpace = false;

        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    buffer.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            buffer.Append(char.ToUpperInvariant(ch));
            previousWasSpace = false;
        }

        var normalized = buffer.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize, int defaultPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = defaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}

