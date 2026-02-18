using System.Collections.Generic;
using System.Threading.Tasks;
using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface IAuditService
{
    Task LogAsync(string action, string? entityType = null, string? entityId = null,
                  string? oldValue = null, string? newValue = null, string? userId = null);
                  
    Task<List<AuditLogEntry>> GetLogsAsync(DateTime startDate, DateTime endDate,
                                            int page = 1, int pageSize = 200);
    
    Task<bool> VerifyChainAsync();
}
