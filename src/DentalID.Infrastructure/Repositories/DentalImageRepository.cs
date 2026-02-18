using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DentalID.Infrastructure.Repositories;

public class DentalImageRepository : IDentalImageRepository
{
    private readonly AppDbContext _db;
    private readonly ILoggerService _logger;

    public DentalImageRepository(AppDbContext db, ILoggerService logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DentalImage?> GetByIdAsync(int id)
        => await _db.DentalImages.FindAsync(id);

    public async Task<List<DentalImage>> GetBySubjectIdAsync(int subjectId, int page = 1, int pageSize = 50)
        => await _db.DentalImages
            .Where(d => d.SubjectId == subjectId)
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

    public async Task<DentalImage> AddAsync(DentalImage image)
    {
        _db.DentalImages.Add(image);
        await _db.SaveChangesAsync();
        return image;
    }

    public async Task UpdateAsync(DentalImage image)
    {
        _db.DentalImages.Update(image);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var img = await _db.DentalImages.FindAsync(id);
        if (img != null)
        {
            // Remove from DB first
            _db.DentalImages.Remove(img);
            await _db.SaveChangesAsync();

            // Delete physical file after DB commit confirmed
            if (!string.IsNullOrEmpty(img.ImagePath) && File.Exists(img.ImagePath))
            {
                try
                {
                    File.Delete(img.ImagePath);
                }
                catch (IOException ex)
                {
                    // Log but don't fail the operation (Orphaned file is better than DB inconsistency)
                    _logger.LogWarning($"Failed to delete file {img.ImagePath}: {ex.Message}");
                }
            }
        }
    }
}

