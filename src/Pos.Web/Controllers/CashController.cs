using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Web.Controllers;

/// <summary>
/// Caja del día: apertura con fondo inicial, ingresos y retiros de efectivo,
/// y el cierre con la entrega del dinero al dueño más el listado de lo vendido.
/// </summary>
public class CashController(
    ApplicationDbContext db,
    ICashSessionService cashSessions,
    ISettingsService settings,
    IReceiptPrinter printer,
    IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var config = await settings.GetAsync();
        var userId = User.GetUserId() ?? 0;
        var session = await cashSessions.GetOpenSessionAsync(userId);

        ViewData["Title"] = "Caja del día";
        ViewBag.Currency = config.Currency;
        ViewBag.DefaultFloat = config.PosDefaultOpeningFloat;
        ViewBag.Summary = session is null ? null : await cashSessions.BuildSummaryAsync(session.Id);
        ViewBag.Recent = await db.CashSessions
            .Where(c => User.HasPermission(Permissions.CashViewAll) || c.UserId == userId)
            .OrderByDescending(c => c.OpenedAt)
            .Take(8)
            .AsNoTracking()
            .ToListAsync();

        return View(session);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.CashOpen)]
    public async Task<IActionResult> Open(decimal openingAmount, string? notes)
    {
        if (openingAmount < 0)
        {
            TempData["Error"] = "El fondo inicial no puede ser negativo.";
            return RedirectToAction(nameof(Index));
        }

        var session = await cashSessions.OpenAsync(
            User.GetUserId() ?? 0, User.GetDisplayName(), openingAmount, notes);

        await audit.LogAsync("CajaAbierta", "Caja", session.Id.ToString(),
            $"Fondo inicial {openingAmount}");

        TempData["Success"] = "Caja abierta. Ya puede vender en el punto de venta.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.CashMovements)]
    public async Task<IActionResult> Movement(int sessionId, string type, decimal amount, string reason)
    {
        var session = await db.CashSessions.FirstOrDefaultAsync(c => c.Id == sessionId);
        if (session is null) return NotFound();

        if (session.Status == CashSessionStatus.Closed)
        {
            TempData["Error"] = "El turno ya está cerrado, no admite más movimientos.";
            return RedirectToAction(nameof(Index));
        }

        if (amount <= 0 || string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Indique un monto mayor que cero y el motivo del movimiento.";
            return RedirectToAction(nameof(Index));
        }

        var movementType = string.Equals(type, "Out", StringComparison.OrdinalIgnoreCase)
            ? CashMovementType.Out
            : CashMovementType.In;

        db.CashMovements.Add(new CashMovement
        {
            CashSessionId = sessionId,
            Type = movementType,
            Amount = amount,
            Reason = reason.Trim(),
            UserId = User.GetUserId() ?? 0,
            UserName = User.GetDisplayName()
        });
        await db.SaveChangesAsync();

        await audit.LogAsync("MovimientoCaja", "Caja", sessionId.ToString(),
            $"{movementType}: {amount} - {reason}");

        TempData["Success"] = movementType == CashMovementType.In
            ? "Ingreso de efectivo registrado."
            : "Retiro de efectivo registrado.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Pantalla de arqueo: se cuenta el efectivo y se calcula la entrega al dueño.</summary>
    [HttpGet]
    [HasPermission(Permissions.CashClose)]
    public async Task<IActionResult> Close(int id)
    {
        var config = await settings.GetAsync();
        var session = await db.CashSessions.FirstOrDefaultAsync(c => c.Id == id);
        if (session is null) return NotFound();

        if (session.UserId != User.GetUserId() && !User.HasPermission(Permissions.CashViewAll))
        {
            return Forbid();
        }

        if (session.Status == CashSessionStatus.Closed)
        {
            return RedirectToAction(nameof(Delivery), new { id });
        }

        ViewData["Title"] = "Cierre y entrega de caja";
        ViewBag.Currency = config.Currency;
        ViewBag.Summary = await cashSessions.BuildSummaryAsync(id);
        return View(session);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.CashClose)]
    public async Task<IActionResult> Close(int id, decimal countedAmount, decimal leftInDrawer, string? notes)
    {
        var session = await db.CashSessions.FirstOrDefaultAsync(c => c.Id == id);
        if (session is null) return NotFound();

        if (session.UserId != User.GetUserId() && !User.HasPermission(Permissions.CashViewAll))
        {
            return Forbid();
        }

        if (countedAmount < 0 || leftInDrawer < 0)
        {
            TempData["Error"] = "Los montos no pueden ser negativos.";
            return RedirectToAction(nameof(Close), new { id });
        }

        if (leftInDrawer > countedAmount)
        {
            TempData["Error"] = "El fondo que queda en caja no puede ser mayor que el efectivo contado.";
            return RedirectToAction(nameof(Close), new { id });
        }

        try
        {
            var closed = await cashSessions.CloseAsync(
                id, User.GetUserId() ?? 0, User.GetDisplayName(), countedAmount, leftInDrawer, notes);

            await audit.LogAsync("CajaCerrada", "Caja", id.ToString(),
                $"Contado {closed.CountedAmount}, esperado {closed.ExpectedAmount}, " +
                $"entregado {closed.DeliveredToOwner}, diferencia {closed.Difference}");

            TempData["Success"] = "Caja cerrada. Imprima el comprobante de entrega para el dueño.";
            return RedirectToAction(nameof(Delivery), new { id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>Comprobante de entrega de caja con el listado de productos vendidos.</summary>
    [HttpGet]
    public async Task<IActionResult> Delivery(int id, bool print = false)
    {
        var config = await settings.GetAsync();
        var session = await db.CashSessions
            .Include(c => c.Movements)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (session is null) return NotFound();

        if (session.UserId != User.GetUserId() && !User.HasPermission(Permissions.CashViewAll))
        {
            return Forbid();
        }

        ViewData["Title"] = $"Entrega de caja #{session.Id}";
        ViewBag.Currency = CurrencyInfo.Get(session.CurrencyCode);
        ViewBag.Settings = config;
        ViewBag.Summary = await cashSessions.BuildSummaryAsync(id);
        ViewBag.Print = print;
        return View(session);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintDelivery(int id)
    {
        var summary = await cashSessions.BuildSummaryAsync(id);
        var result = await printer.PrintCashCloseAsync(summary);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Delivery), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> History(DateTime? from, DateTime? to)
    {
        var config = await settings.GetAsync();
        var userId = User.GetUserId() ?? 0;
        var canSeeAll = User.HasPermission(Permissions.CashViewAll);

        var start = (from ?? DateTime.Now.AddDays(-30)).Date;
        var end = (to ?? DateTime.Now).Date.AddDays(1);

        var query = db.CashSessions
            .Where(c => c.OpenedAt >= start.ToUniversalTime() && c.OpenedAt < end.ToUniversalTime());
        if (!canSeeAll) query = query.Where(c => c.UserId == userId);

        ViewData["Title"] = "Historial de cajas";
        ViewBag.Currency = config.Currency;
        ViewBag.From = start;
        ViewBag.To = end.AddDays(-1);

        return View(await query.OrderByDescending(c => c.OpenedAt).AsNoTracking().ToListAsync());
    }
}
