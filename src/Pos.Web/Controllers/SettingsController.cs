using Microsoft.AspNetCore.Mvc;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Web.Controllers;

/// <summary>
/// Configuración del sistema. Solo el administrador entra aquí: desde este menú
/// se define la moneda (soles peruanos o pesos colombianos), los datos del
/// negocio, el impuesto y la impresora térmica.
/// </summary>
[HasPermission(Permissions.SettingsManage)]
public class SettingsController(
    ISettingsService settings,
    IReceiptPrinter printer,
    IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Configuración";
        ViewBag.Currencies = CurrencyInfo.Supported;
        return View(await settings.GetAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(PosSettings model)
    {
        ViewData["Title"] = "Configuración";
        ViewBag.Currencies = CurrencyInfo.Supported;

        if (CurrencyInfo.Supported.All(c => c.Code != model.CurrencyCode))
        {
            ModelState.AddModelError(nameof(model.CurrencyCode), "La moneda seleccionada no está soportada.");
        }
        if (model.TaxRate is < 0 or > 100)
        {
            ModelState.AddModelError(nameof(model.TaxRate), "El impuesto debe estar entre 0 y 100.");
        }
        if (model.PrinterPort is < 1 or > 65535)
        {
            ModelState.AddModelError(nameof(model.PrinterPort), "El puerto debe estar entre 1 y 65535.");
        }
        if (model.PrinterCharsPerLine is < 32 or > 56)
        {
            ModelState.AddModelError(nameof(model.PrinterCharsPerLine),
                "El ancho debe estar entre 32 y 56 caracteres (48 para papel de 80 mm).");
        }

        if (!ModelState.IsValid) return View(model);

        var previous = await settings.GetAsync();
        await settings.SaveAsync(model, User.GetUserId(), User.GetDisplayName());

        if (previous.CurrencyCode != model.CurrencyCode)
        {
            await audit.LogAsync("MonedaCambiada", "Configuracion", null,
                $"{previous.CurrencyCode} -> {model.CurrencyCode}");
            TempData["Success"] =
                $"Configuración guardada. La moneda del sistema ahora es {CurrencyInfo.Get(model.CurrencyCode).Name}. " +
                "Las ventas anteriores conservan la moneda con la que se cobraron.";
        }
        else
        {
            await audit.LogAsync("ConfiguracionGuardada", "Configuracion", null, "Actualización de parámetros");
            TempData["Success"] = "Configuración guardada.";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Imprime una línea de prueba para verificar la impresora de red.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestPrinter()
    {
        var result = await printer.PrintTestAsync();
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenDrawer()
    {
        var result = await printer.OpenDrawerAsync();
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
