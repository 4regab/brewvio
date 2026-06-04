using Brewvio.Dtos;
using Brewvio.Services;
using Microsoft.AspNetCore.Mvc;

namespace Brewvio.Controllers;

// Shift management — any authenticated user manages their own shift.
[ApiController]
[Route("api/shifts")]
public class ShiftsController(ShiftService shifts) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> Current() =>
        await shifts.GetCurrentAsync() is { } s ? Ok(s) : NoContent();

    [HttpPost("start")]
    public async Task<IActionResult> Start(StartShiftRequest req) => Ok(await shifts.StartAsync(req.StartingCash));

    [HttpPost("end")]
    public async Task<IActionResult> End(EndShiftRequest req) =>
        await shifts.EndAsync(req.EndingCash) is { } s ? Ok(s) : BadRequest(new { message = "No open shift to close." });
}
