using Microsoft.AspNetCore.Mvc;

namespace QuickMemoryServer.Worker.Controllers;

public sealed class AdminController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        return View();
    }
}

