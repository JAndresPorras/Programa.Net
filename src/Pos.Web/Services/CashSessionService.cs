using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Web.Services;

/// <summary>Resumen del turno usado en la pantalla de cierre y en el reporte de entrega.</summary>
public class CashSessionSummary
{
    public CashSession Session { get; set; } = null!;
    public decimal OpeningAmount { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal TransferSales { get; set; }
    public decimal OtherSales { get; set; }
    public decimal MovementsIn { get; set; }
    public decimal MovementsOut { get; set; }
    public decimal CancelledTotal { get; set; }
    public int SalesCount { get; set; }
    public int CancelledCount { get; set; }
    public decimal UnitsSold { get; set; }

    /// <summary>Dinero que deberia haber fisicamente en la caja.</summary>
    public decimal ExpectedCash => OpeningAmount + CashSales + MovementsIn - MovementsOut;

    /// <summary>Total vendido sin importar el medio de pago.</summary>
    public decimal TotalSales => CashSales + CardSales + TransferSales + OtherSales;

    /// <summary>Lo que se le entrega al dueno: el efectivo esperado menos el fondo que queda.</summary>
    public decimal SuggestedDelivery(decimal leftInDrawer) => Math.Max(0, ExpectedCash - leftInDrawer);

    public List<SoldProductRow> Products { get; set; } = [];
}

/// <summary>Fila del listado de productos vendidos.</summary>
public class SoldProductRow
{
    public int? ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Unit { get; set; } = "UND";
    public decimal Quantity { get; set; }
    public decimal Total { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal MinStock { get; set; }
    public bool IsProduced { get; set; }
}

public interface ICashSessionService
{
    Task<CashSession?> GetOpenSessionAsync(int userId, CancellationToken ct = default);
    Task<CashSession> OpenAsync(int userId, string userName, decimal openingAmount, string? notes, CancellationToken ct = default);
    Task<CashSessionSummary> BuildSummaryAsync(int sessionId, CancellationToken ct = default);
    Task<CashSession> CloseAsync(int sessionId, int userId, string userName, decimal countedAmount, decimal leftInDrawer, string? notes, CancellationToken ct = default);
    Task<decimal> GetCollectedAsync(int userId, CancellationToken ct = default);
}

/// <summary>
/// Maneja el turno de caja: apertura con fondo inicial, movimientos de efectivo
/// y el cierre con la entrega de dinero al dueno al final del dia.
/// </summary>
public class CashSessionService(
    ApplicationDbContext db,
    ISettingsService settings,
    ILogger<CashSessionService> logger) : ICashSessionService
{
    public Task<CashSession?> GetOpenSessionAsync(int userId, CancellationToken ct = default) =>
        db.CashSessions
            .Where(c => c.UserId == userId && c.Status == CashSessionStatus.Open)
            .OrderByDescending(c => c.OpenedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<CashSession> OpenAsync(
        int userId, string userName, decimal openingAmount, string? notes, CancellationToken ct = default)
    {
        var existing = await GetOpenSessionAsync(userId, ct);
        if (existing is not null) return existing;

        var config = await settings.GetAsync(ct);
        var session = new CashSession
        {
            UserId = userId,
            UserName = userName,
            OpenedAt = DateTime.UtcNow,
            OpeningAmount = config.Currency.Round(openingAmount),
            CurrencyCode = config.CurrencyCode,
            Notes = notes,
            Status = CashSessionStatus.Open
        };

        db.CashSessions.Add(session);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Caja abierta por {User} con fondo {Amount}", userName, openingAmount);
        return session;
    }

    /// <summary>Dinero en efectivo acumulado en el turno abierto (el que muestra el ojito del POS).</summary>
    public async Task<decimal> GetCollectedAsync(int userId, CancellationToken ct = default)
    {
        var session = await GetOpenSessionAsync(userId, ct);
        if (session is null)
        {
            // Sin turno abierto se muestra lo vendido en efectivo el dia de hoy.
            var today = DateTime.UtcNow.Date;
            return await db.Sales
                .Where(s => s.UserId == userId && s.Status == SaleStatus.Completed &&
                            s.PaymentMethod == PaymentMethod.Cash && s.SoldAt >= today)
                .SumAsync(s => s.Total, ct);
        }

        var summary = await BuildSummaryAsync(session.Id, ct);
        return summary.ExpectedCash;
    }

    public async Task<CashSessionSummary> BuildSummaryAsync(int sessionId, CancellationToken ct = default)
    {
        var session = await db.CashSessions
            .Include(c => c.Movements)
            .FirstOrDefaultAsync(c => c.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"El turno {sessionId} no existe");

        var sales = await db.Sales
            .Where(s => s.CashSessionId == sessionId)
            .Include(s => s.Items)
            .AsNoTracking()
            .ToListAsync(ct);

        var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();
        var cancelled = sales.Where(s => s.Status == SaleStatus.Cancelled).ToList();

        var summary = new CashSessionSummary
        {
            Session = session,
            OpeningAmount = session.OpeningAmount,
            CashSales = completed.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.Total),
            CardSales = completed.Where(s => s.PaymentMethod == PaymentMethod.Card).Sum(s => s.Total),
            TransferSales = completed.Where(s => s.PaymentMethod == PaymentMethod.Transfer).Sum(s => s.Total),
            OtherSales = completed.Where(s => s.PaymentMethod == PaymentMethod.Mixed).Sum(s => s.Total),
            MovementsIn = session.Movements.Where(m => m.Type == CashMovementType.In).Sum(m => m.Amount),
            MovementsOut = session.Movements.Where(m => m.Type == CashMovementType.Out).Sum(m => m.Amount),
            SalesCount = completed.Count,
            CancelledCount = cancelled.Count,
            CancelledTotal = cancelled.Sum(s => s.Total),
            UnitsSold = completed.SelectMany(s => s.Items).Sum(i => i.Quantity)
        };

        summary.Products = BuildProductRows(completed);
        await FillStockAsync(summary.Products, ct);
        return summary;
    }

    internal static List<SoldProductRow> BuildProductRows(IEnumerable<Sale> sales) =>
        sales.SelectMany(s => s.Items)
            .GroupBy(i => new { i.ProductId, i.ProductCode, i.ProductName, i.CategoryName, i.Unit })
            .Select(g => new SoldProductRow
            {
                ProductId = g.Key.ProductId,
                Code = g.Key.ProductCode,
                Name = g.Key.ProductName,
                CategoryName = g.Key.CategoryName,
                Unit = g.Key.Unit,
                Quantity = g.Sum(i => i.Quantity),
                Total = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(r => r.Quantity)
            .ToList();

    private async Task FillStockAsync(List<SoldProductRow> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.ProductId.HasValue).Select(r => r.ProductId!.Value).ToList();
        if (ids.Count == 0) return;

        var products = await db.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Stock, p.MinStock, p.IsProduced })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var product = products.FirstOrDefault(p => p.Id == row.ProductId);
            if (product is null) continue;
            row.CurrentStock = product.Stock;
            row.MinStock = product.MinStock;
            row.IsProduced = product.IsProduced;
        }
    }

    public async Task<CashSession> CloseAsync(
        int sessionId, int userId, string userName,
        decimal countedAmount, decimal leftInDrawer, string? notes, CancellationToken ct = default)
    {
        var session = await db.CashSessions.FirstOrDefaultAsync(c => c.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"El turno {sessionId} no existe");

        if (session.Status == CashSessionStatus.Closed)
            throw new InvalidOperationException("El turno ya fue cerrado.");

        var summary = await BuildSummaryAsync(sessionId, ct);
        var currency = (await settings.GetAsync(ct)).Currency;

        session.ClosedAt = DateTime.UtcNow;
        session.CountedAmount = currency.Round(countedAmount);
        session.ExpectedAmount = currency.Round(summary.ExpectedCash);
        session.Difference = currency.Round(countedAmount - summary.ExpectedCash);
        session.LeftInDrawer = currency.Round(leftInDrawer);
        session.DeliveredToOwner = currency.Round(Math.Max(0, countedAmount - leftInDrawer));
        session.ClosedByUserId = userId;
        session.ClosedByUserName = userName;
        session.Status = CashSessionStatus.Closed;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            session.Notes = string.IsNullOrWhiteSpace(session.Notes) ? notes : $"{session.Notes}\n{notes}";
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Caja {Id} cerrada por {User}. Esperado {Expected}, contado {Counted}, entregado {Delivered}",
            sessionId, userName, session.ExpectedAmount, session.CountedAmount, session.DeliveredToOwner);

        return session;
    }
}
