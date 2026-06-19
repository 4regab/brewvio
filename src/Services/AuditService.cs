using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Audit Logger engine — records critical actions with timestamp and acting user.
public class AuditService(BrewvioDbContext db, CurrentUser current)
{
    // Enqueues an audit entry on the shared context; the calling service persists it
    // in the same SaveChanges so the action and its audit row commit atomically.
    public void Add(string action, string details) => Add(action, details, null);

    // Overload that tags the entry with the ingredient it concerns, so stock movements
    // (StockIn/StockOut/InventoryAdjust/StockSale/StockRefund) are queryable per ingredient.
    // Returns the staged entity so a caller running a concurrency-retry loop can detach it.
    public AuditLog Add(string action, string details, int? ingredientId)
    {
        var log = new AuditLog
        {
            UserId = current.Id == 0 ? null : current.Id,
            Username = current.Username,
            Action = action,
            Details = details,
            IngredientId = ingredientId
        };
        db.AuditLogs.Add(log);
        return log;
    }

    // Enqueues and immediately persists (for standalone events like login).
    public async Task LogAsync(string action, string details, CancellationToken ct = default)
    {
        Add(action, details);
        await db.SaveChangesAsync(ct);
    }

    // Lists recent audit entries (newest first), excluding high-volume stock movement rows.
    // take: maximum number of entries to return.
    // ct: cancellation token.
    // returns: a list of audit log DTOs.
    public async Task<List<AuditLogDto>> ListAsync(int take = 200, CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking()
            // Hide high-volume per-ingredient sale/refund movement rows from the general audit
            // log; they stay available in the per-ingredient stock history. -> SQL NOT IN (...).
            .Where(a => !StockActions.AuditExcluded.Contains(a.Action))
            .OrderByDescending(a => a.Timestamp).Take(take)
            .Select(a => new AuditLogDto(a.Id, a.Timestamp, a.Username, a.Action, a.Details))
            .ToListAsync(ct);
}
