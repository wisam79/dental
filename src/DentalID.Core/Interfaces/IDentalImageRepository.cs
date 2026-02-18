using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface IDentalImageRepository
{
    Task<DentalImage?> GetByIdAsync(int id);
    Task<List<DentalImage>> GetBySubjectIdAsync(int subjectId, int page = 1, int pageSize = 50);
    Task<DentalImage> AddAsync(DentalImage image);
    Task UpdateAsync(DentalImage image);
    Task DeleteAsync(int id);
}
