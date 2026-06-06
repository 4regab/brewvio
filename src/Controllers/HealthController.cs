using Brewvio.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController(BrewvioDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var dbOk = await db.Database.CanConnectAsync(ct);
        return Ok(new { status = "ok", service = "brewvio-api", database = dbOk ? "connected" : "unreachable" });
    }
}
