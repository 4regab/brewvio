using System.Text.Json;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Store settings endpoints — public store info plus manager-only configuration and backup.
[ApiController]
[Route("api/settings")]
public class SettingsController(SettingsService settings) : ControllerBase
{
    // Public store info (name + currency) — any authenticated user (the POS needs it).
    [HttpGet("store")]
    public async Task<IActionResult> Store(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        return Ok(new { storeName = s.StoreName, address = s.Address, currency = s.Currency, taxRatePercent = s.TaxRatePercent });
    }

    // Gets the full store settings (Manager).
    // ct: cancellation token
    // returns: 200 OK with the store settings
    [HttpGet, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await settings.GetAsync(ct));

    // Updates the store settings (Manager).
    // dto: the new store settings values
    // ct: cancellation token
    // returns: 200 OK with the updated store settings
    [HttpPut, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(StoreSettingsDto dto, CancellationToken ct) => Ok(await settings.UpdateAsync(dto, ct));

    // Data backup (USB-backup equivalent for the web stack): downloadable JSON snapshot.
    [HttpGet("backup"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Backup(CancellationToken ct)
    {
        var data = await settings.ExportBackupAsync(ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });
        return File(json, "application/json", $"brewvio-backup-{DateTime.UtcNow:yyyyMMdd-HHmm}.json");
    }
}
