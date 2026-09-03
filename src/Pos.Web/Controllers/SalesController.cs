using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Web.Controllers;

/// <summary>Recibos emitidos: listado del día, detalle, impresión y anulación.</summary>
public class SalesController(
    ApplicationDbContext db,
    ISettingsService settings,
    ISaleService sales,
    IReceiptPrinter printer,
    IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? date, int? userId)
    {
        var config = await settings.GetAsync();
        var day = (date ?? DateTime.Now).Date;
        var from = day.ToUniversalTime();
        var to = day.AddDays(1).ToUniversalTime();

        var canSeeAll = User.HasAnyPermission(Permissions.ReportsSales, Permissions.CashViewAll);
        var currentUserId = User.GetUserId() ?? 0;

        var query = db.Sales.Include(s => s.Items).Where(s => s.SoldAt >= from && s.SoldAt < to);
        if (!canSeeAll) query = query.Where(s => s.UserId == currentUserId);
        else if (userId.HasValue) query = query.Where(s => s.UserId == userId.Value);

        var list = await query.OrderByDescending(s => s.SoldAt).AsNoTracking().ToListAsync();

        ViewData["Title"] = "Ventas del día";
        ViewData["Subtitle"] = day.ToString("dddd dd 'de' MMMM 'de' yyyy");
        ViewBag.Currency = config.Currency;
        ViewBag.Date = day;
        ViewBag.CanSeeAll = canSeeAll;
        ViewBag.SelectedUser = userId;
        ViewBag.Users = canSeeAll
            ? await db.Users.OrderBy(u => u.FullName)
                .Select(u => new SelectListItem(u.FullName, u.Id.ToString(), u.Id == userId))
                .ToListAsync()
            : null;

        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var config = await settings.GetAsync();
        var sale = await db.Sales
            .Include(s => s.Items)
            .Include(s => s.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale is null) return NotFound();

        if (sale.UserId != User.GetUserId() &&
            !User.HasAnyPermission(Permissions.ReportsSales, Permissions.CashViewAll))
        {
            return Forbid();
        }

        ViewData["Title"] = $"Recibo {sale.Number}";
        ViewBag.Currency = config.Currency;
        ViewBag.Settings = config;
        return View(sale);
    }

    /// <summary>
    /// Recibo formateado para papel térmico de 80 mm. Con ?auto=1 se manda a
    /// imprimir solo, que es como lo llama el punto de venta al cerrar la venta.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Receipt(int id, bool auto = false)
    {
        var config = await settings.GetAsync();
        var sale = await db.Sales
            .Include(s => s.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale is null) return NotFound();

        if (sale.UserId != User.GetUserId() &&
            !User.HasAnyPermission(Permissions.ReportsSales, Permissions.CashViewAll))
        {
            return Forbid();
        }

        ViewBag.Currency = CurrencyInfo.Get(sale.CurrencyCode);
        ViewBag.Settings = config;
        ViewBag.Auto = auto;
        ViewBag.Copies = Math.Clamp(config.ReceiptCopies, 1, 3);
        return View(sale);
    }

    /// <summary>Envía el recibo por red a la impresora térmica (ESC/POS, puerto 9100).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintNetwork(int id)
    {
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
        if (sale is null) return NotFound();

        var result = await printer.PrintSaleAsync(sale);
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new { success = result.Success, message = result.Message });
        }

        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [HasPermission(Permissions.PosCancelSale)]
    public async Task<IActionResult> Cancel(int id)
    {
        var config = await settings.GetAsync();
        var sale = await db.Sales.Include(s => s.Items).AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (sale is null) return NotFound();

        ViewData["Title"] = $"Anular recibo {sale.Number}";
        ViewBag.Currency = config.Currency;
        return View(sale);
    }

    [HttpPost, ActionName("Cancel")]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.PosCancelSale)]
    public async Task<IActionResult> CancelConfirmed(int id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Debe indicar el motivo de la anulación.";
            return RedirectToAction(nameof(Cancel), new { id });
        }

        var result = await sales.CancelAsync(id, User.GetUserId() ?? 0, User.GetDisplayName(), reason.Trim());
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        await audit.LogAsync("VentaAnulada", "Venta", id.ToString(), $"Motivo: {reason}");
        TempData["Success"] = $"El recibo {result.Sale!.Number} fue anulado.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
