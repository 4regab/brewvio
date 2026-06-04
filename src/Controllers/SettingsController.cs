using System.Text.Json;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(SettingsService settings) : ControllerBase
{
    // Public store info (name + currency) — any authenticated user (the POS needs it).
    [HttpGet("store")]
    public async Task<IActionResult> Store()
    {
        var s = await settings.GetAsync();
        return Ok(new { storeName = s.StoreName, address = s.Address, currency = s.Currency, taxRatePercent = s.TaxRatePercent });
    }

    [HttpGet, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Get() => Ok(await settings.GetAsync());

    [HttpPut, Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(StoreSettingsDto dto) => Ok(await settings.UpdateAsync(dto));

    // Data backup (USB-backup equivalent for the web stack): downloadable JSON snapshot.
    [HttpGet("backup"), Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Backup()
    {
        var data = await settings.ExportBackupAsync();
        var json = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });
        return File(json, "application/json", $"brewvio-backup-{DateTime.UtcNow:yyyyMMdd-HHmm}.json");
    }
}
