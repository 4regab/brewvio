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
    public void Add(string action, string details) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = current.Id == 0 ? null : current.Id,
            Username = current.Username,
            Action = action,
            Details = details
        });

    // Enqueues and immediately persists (for standalone events like login).
    public async Task LogAsync(string action, string details, CancellationToken ct = default)
    {
        Add(action, details);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditLogDto>> ListAsync(int take = 200, CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Timestamp).Take(take)
            .Select(a => new AuditLogDto(a.Id, a.Timestamp, a.Username, a.Action, a.Details))
            .ToListAsync(ct);
}
