using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Pos.Web.Controllers;

/// <summary>
/// Pagina a la que el manejador de errores de produccion redirige cuando algo falla
/// (ver UseExceptionHandler en Program.cs). Sin ella, un error mostraba un 404.
/// </summary>
public class HomeController : Controller
{
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["Title"] = "Ocurrió un error";
        return View("~/Views/Shared/Error.cshtml");
    }
}
