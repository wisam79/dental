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

        // Optimization 1: ID search (SUB-...) is unencrypted and indexed
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

        // Optimization 2: Exact hash match (deterministic lookup)
        var normalizedName = NormalizeFullName(query);
        var nameHash = normalizedName != null ? _encryptionService.ComputeDeterministicHash(normalizedName, FullNameHashContext) : "INVALID_HASH";

        var normalizedId = NormalizeNationalId(query);
        var idHash = normalizedId != null ? _encryptionService.ComputeDeterministicHash(normalizedId, NationalIdHashContext) : "INVALID_HASH";

        var exactMatches = await _db.Subjects
            .Include(s => s.DentalImages)
            .Where(s => s.FullNameLookupHash == nameHash || s.NationalIdLookupHash == idHash)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        if (exactMatches.Count >= pageSize)
        {
            return exactMatches.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        // Bug Fix #54: Partial Match Fallback (Client-side decryption)
        // Fetches a recent window of subjects to allow "Contains" search on encrypted PII.
        // Limited to 250 candidates to prevent LOH/GC pressure in large databases.
        var candidates = await _db.Subjects
            .Include(s => s.DentalImages)
            .OrderByDescending(s => s.CreatedAt)
            .Take(250)
            .ToListAsync();

        var partialMatches = candidates
            .Where(s => !exactMatches.Any(e => e.Id == s.Id)) // Avoid duplicates
            .Where(s => (normalizedName != null && NormalizeFullName(s.FullName)?.Contains(normalizedName) == true) ||
                        (normalizedId != null && NormalizeNationalId(s.NationalId)?.Contains(normalizedId) == true))
            .ToList();

        return exactMatches.Concat(partialMatches)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public async Task<int> GetSearchCountAsync(string query)
    {
        // For simplicity in the UI, we return the count of the hybrid search result
        // Note: In very large databases, this might be a slight under-count if matches 
        // exist beyond the 250-candidate window.
        var results = await SearchAsync(query, 1, MaxPageSize);
        return results.Count;
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
        
        // Bug Fix #55: Handle Tracked vs Detached Entities safely
        var local = _db.Subjects.Local.FirstOrDefault(entry => entry.Id == subject.Id);
        if (local != null)
        {
             _db.Entry(local).State = EntityState.Detached;
        }

        var oldVersion = subject.RowVersion; 
        subject.RowVersion = Guid.NewGuid().ToByteArray(); // Rotate token
        
        var entry = _db.Subjects.Update(subject);
        entry.Property(p => p.RowVersion).OriginalValue = oldVersion;

        try 
        {
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
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
            var filesToDelete = new List<string>();
            foreach (var img in subject.DentalImages)
            {
                if (!string.IsNullOrEmpty(img.ImagePath)) filesToDelete.Add(img.ImagePath);
                // Bug Fix #56: Also cleanup thumbnails
                var thumbPath = Path.Combine(Path.GetDirectoryName(img.ImagePath) ?? "", "thumbs", Path.GetFileName(img.ImagePath) ?? "");
                if (File.Exists(thumbPath)) filesToDelete.Add(thumbPath);
            }
                
            _db.Subjects.Remove(subject);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            foreach (var path in filesToDelete)
            {
                if (System.IO.File.Exists(path))
                {
                    try { System.IO.File.Delete(path); } catch { /* Log failure in production */ }
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

