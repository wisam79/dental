using DentalID.Core.Entities;
using DentalID.Core.Enums;

namespace DentalID.Core.Interfaces;

public interface ICaseRepository
{
    Task<Case?> GetByIdAsync(int id);
    Task<Case?> GetByCaseNumberAsync(string caseNumber);
    Task<List<Case>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<List<Case>> GetByStatusAsync(CaseStatus status);
    Task<List<Case>> GetActiveCasesAsync(int page = 1, int pageSize = 20);
    Task<int> GetCountAsync(CaseStatus? status = null);
    Task<Case> AddAsync(Case forensicCase);
    Task UpdateAsync(Case forensicCase);
    Task DeleteAsync(int id);
    Task<string> GenerateCaseNumberAsync();
}
