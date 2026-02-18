using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface IMatchRepository
{
    Task<Match?> GetByIdAsync(int id);
    Task<List<Match>> GetByCaseIdAsync(int caseId);
    Task<List<Match>> GetBySubjectIdAsync(int subjectId);
    Task<Match> AddAsync(Match match);
    Task UpdateAsync(Match match);
    Task<List<Match>> GetRecentAsync(int count = 10);
    Task DeleteAsync(int id);
}
