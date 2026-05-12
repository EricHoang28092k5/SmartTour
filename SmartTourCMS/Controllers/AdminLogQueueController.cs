using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartTourCMS.Models;
using SmartTourCMS.Services;

namespace SmartTourCMS.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminLogQueueController : Controller
{
    private readonly ILogRunner _logRunner;

    public AdminLogQueueController(ILogRunner logRunner) => _logRunner = logRunner;

    [HttpGet]
    public IActionResult Simulator() =>
        Redirect(Url.Action("GeofenceSimulator", "LoadTest") + "#gf-log-audio-panel");

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Overwrite([FromBody] LogQueueOverwriteRequest? body)
    {
        body ??= new LogQueueOverwriteRequest();
        await _logRunner.OverwriteAsync(body.Text ?? string.Empty, body.SessionId).ConfigureAwait(false);
        return Json(new { ok = true, path = _logRunner.LogFilePath });
    }

    [HttpGet]
    public async Task<IActionResult> Download()
    {
        var bytes = await _logRunner.ReadBytesAsync().ConfigureAwait(false);
        return File(bytes, "text/plain", "logqueue.txt");
    }

    [HttpGet]
    public async Task<IActionResult> Text()
    {
        var text = await _logRunner.ReadAsync().ConfigureAwait(false);
        return Json(new { text });
    }
}
