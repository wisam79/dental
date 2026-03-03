using Microsoft.EntityFrameworkCore;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DentalID.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    // Bug #24 fix: Serialize chain-hash writes to prevent race conditions
    private static readonly SemaphoreSlim _chainLock = new(1, 1);

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<AuditLogEntry>> GetLogsAsync(DateTime startDate, DateTime endDate,
        int page = 1, int pageSize = 200)
    {
        // Bug #65: Add pagination to avoid loading millions of audit rows into memory
        return await _db.AuditLog
            .Where(x => x.Timestamp >= startDate && x.Timestamp <= endDate)
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task LogAsync(string action, string? entityType = null, string? entityId = null,
                               string? oldValue = null, string? newValue = null, string? userId = null)
    {
        // Bug #24 fix: Acquire lock to serialize chain hash computation
        await _chainLock.WaitAsync();
        try
        {
            // 1. Get Last Hash
            var lastEntry = await _db.AuditLog.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            var prevHash = lastEntry?.Hash ?? "GENESIS";

            // 2. Parse IDs (Best Effort)
            int? eId = int.TryParse(entityId, out var eid) ? eid : null;
            int? uId = int.TryParse(userId, out var uid) ? uid : null;

            var entry = new AuditLogEntry
            {
                Action = action,
                EntityType = entityType,
                EntityId = eId,
                OldValue = oldValue,
                NewValue = newValue,
                UserId = uId,
                Timestamp = DateTime.UtcNow,
                PreviousHash = prevHash
            };

            // 3. Compute Hash
            entry.Hash = ComputeHash(entry);

            _db.AuditLog.Add(entry);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }
        finally
        {
            _chainLock.Release();
        }
    }

    public async Task<bool> VerifyChainAsync()
    {
        var logs = await _db.AuditLog.OrderBy(x => x.Id).ToListAsync();
        if (!logs.Any()) return true;

        string expectedPrevHash = "GENESIS";

        foreach (var log in logs)
        {
            if (log.PreviousHash != expectedPrevHash) return false;
            
            var calculatedHash = ComputeHash(log);
            if (log.Hash != calculatedHash) return false;

            expectedPrevHash = calculatedHash;
        }

        return true;
    }

    private string ComputeHash(AuditLogEntry entry)
    {
        // Bug #66: Use explicit null-safe string representations for nullable int fields
        // to ensure consistent hash across different null representations
        var rawData = $"{entry.PreviousHash}|{entry.Timestamp:O}|{entry.Action}|{entry.EntityType ?? ""}|{entry.EntityId?.ToString() ?? ""}|{entry.UserId?.ToString() ?? ""}|{entry.OldValue ?? ""}|{entry.NewValue ?? ""}";
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes);
    }
}
