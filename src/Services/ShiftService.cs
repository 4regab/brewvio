using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Services;

// Shift management — open/close a cashier shift and summarise its sales & cash position.
public class ShiftService(BrewvioDbContext db, CurrentUser current, AuditService audit)
{
    public async Task<ShiftDto?> GetCurrentAsync()
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.CashierId == current.Id && s.Status == "Open");
        return shift is null ? null : await ToDtoAsync(shift);
    }

    public async Task<ShiftDto> StartAsync(decimal startingCash)
    {
        var existing = await db.Shifts.FirstOrDefaultAsync(s => s.CashierId == current.Id && s.Status == "Open");
        if (existing is not null) return await ToDtoAsync(existing);   // idempotent: one open shift per cashier
        var shift = new Shift { CashierId = current.Id, StartingCash = startingCash, Status = "Open" };
        db.Shifts.Add(shift);
        audit.Add("ShiftStarted", $"Starting cash {startingCash:0.00}");
        await db.SaveChangesAsync();
        return await ToDtoAsync(shift);
    }

    public async Task<ShiftDto?> EndAsync(decimal endingCash)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.CashierId == current.Id && s.Status == "Open");
        if (shift is null) return null;
        shift.EndTime = DateTime.UtcNow;
        shift.EndingCash = endingCash;
        shift.Status = "Closed";
        audit.Add("ShiftEnded", $"Ending cash {endingCash:0.00}");
        await db.SaveChangesAsync();
        return await ToDtoAsync(shift);
    }

    private async Task<ShiftDto> ToDtoAsync(Shift s)
    {
        var cashier = await db.Users.Where(u => u.Id == s.CashierId)
            .Select(u => u.FullName != "" ? u.FullName : u.Username).FirstOrDefaultAsync() ?? "";
        var txns = await db.Transactions.Include(t => t.Payments)
            .Where(t => t.ShiftId == s.Id && t.Status == "Completed").ToListAsync();
        var totalSales = txns.Sum(t => t.TotalAmount);
        var cash = txns.SelectMany(t => t.Payments).Where(p => p.Method == "Cash").Sum(p => p.Amount);
        var card = txns.SelectMany(t => t.Payments).Where(p => p.Method == "Card").Sum(p => p.Amount);
        var expectedCash = s.StartingCash + cash;
        decimal? variance = s.EndingCash.HasValue ? s.EndingCash.Value - expectedCash : null;
        return new ShiftDto(s.Id, cashier, s.StartTime, s.EndTime, s.StartingCash, s.EndingCash, s.Status,
            totalSales, txns.Count, cash, card, expectedCash, variance);
    }
}
