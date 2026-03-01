using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface ISubjectRepository
{
    Task<Subject?> GetByIdAsync(int id);
    Task<Subject?> GetBySubjectIdAsync(string subjectId);
    Task<List<Subject>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<List<Subject>> GetAllWithVectorsAsync();
    IAsyncEnumerable<Subject> StreamAllWithVectorsAsync();
    Task<List<Subject>> SearchAsync(string query, int page = 1, int pageSize = 20);
    Task<int> GetSearchCountAsync(string query);
    Task<int> GetCountAsync();
    Task<Subject> AddAsync(Subject subject);
    Task AddBatchAsync(IEnumerable<Subject> subjects);
    Task UpdateAsync(Subject subject);
    Task<Subject?> GetByNationalIdAsync(string nationalId);
    Task<Subject?> GetByFullNameExactAsync(string fullName);
    Task<List<string>> GetExistingSubjectIdsAsync(IEnumerable<string> subjectIds);
    Task<Subject?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<Subject, bool>> predicate);
    Task DeleteAsync(int id);
}
