using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Web.Services;

public class SalesReport
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal Total { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discounts { get; set; }
    public decimal Tax { get; set; }
    public int SalesCount { get; set; }
    public decimal UnitsSold { get; set; }
    public int CancelledCount { get; set; }
    public decimal CancelledTotal { get; set; }
    public decimal AverageTicket => SalesCount == 0 ? 0 : Total / SalesCount;

    public List<GroupRow> ByPaymentMethod { get; set; } = [];
    public List<GroupRow> ByUser { get; set; } = [];
    public List<GroupRow> ByCategory { get; set; } = [];
    public List<GroupRow> ByDay { get; set; } = [];
    public List<GroupRow> ByHour { get; set; } = [];
    public List<SoldProductRow> TopProducts { get; set; } = [];
}

public record GroupRow(string Label, decimal Amount, int Count, decimal Units = 0);

/// <summary>Fila del pedido a la planta de produccion.</summary>
public class ProductionRow
{
    public int ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Unit { get; set; } = "UND";
    public decimal SoldInPeriod { get; set; }
    public decimal SoldToday { get; set; }
    public decimal AveragePerDay { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal MinStock { get; set; }

    /// <summary>Demanda estimada para el proximo dia (lo mayor entre el promedio y lo vendido hoy).</summary>
    public decimal Forecast => Math.Max(AveragePerDay, SoldToday);

    /// <summary>Cantidad que hay que pedir a la planta para cubrir la demanda y el stock minimo.</summary>
    public decimal SuggestedOrder
    {
        get
        {
            var needed = Forecast + MinStock - CurrentStock;
            return needed <= 0 ? 0 : Math.Ceiling(needed);
        }
    }

    public bool IsUrgent => CurrentStock <= MinStock;
}

public class ProductionReport
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int Days { get; set; } = 1;
    public List<ProductionRow> Rows { get; set; } = [];
    public decimal TotalSuggested => Rows.Sum(r => r.SuggestedOrder);
    public int UrgentCount => Rows.Count(r => r.IsUrgent);
}

public interface IReportService
{
    Task<SalesReport> BuildSalesReportAsync(DateTime fromUtc, DateTime toUtc, int? userId, int? categoryId, CancellationToken ct = default);
    Task<ProductionReport> BuildProductionReportAsync(DateTime fromUtc, DateTime toUtc, int? categoryId, bool onlyProduced, CancellationToken ct = default);
}

/// <summary>
/// Arma los reportes de ventas y el pedido a la planta de produccion
/// a partir de los recibos cerrados.
/// </summary>
public class ReportService(ApplicationDbContext db) : IReportService
{
    public async Task<SalesReport> BuildSalesReportAsync(
        DateTime fromUtc, DateTime toUtc, int? userId, int? categoryId, CancellationToken ct = default)
    {
        var query = db.Sales
            .Include(s => s.Items)
            .Where(s => s.SoldAt >= fromUtc && s.SoldAt < toUtc);

        if (userId.HasValue) query = query.Where(s => s.UserId == userId.Value);

        var sales = await query.AsNoTracking().ToListAsync(ct);

        if (categoryId.HasValue)
        {
            var categoryName = await db.Categories
                .Where(c => c.Id == categoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);

            if (categoryName is not null)
            {
                foreach (var sale in sales)
                {
                    var kept = sale.Items.Where(i => i.CategoryName == categoryName).ToList();
                    sale.Items = kept;
                }
                sales = sales.Where(s => s.Items.Count > 0).ToList();
            }
        }

        var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();
        var cancelled = sales.Where(s => s.Status == SaleStatus.Cancelled).ToList();

        // Al filtrar por categoria los totales se calculan sobre las lineas visibles.
        decimal TotalOf(Sale s) => categoryId.HasValue ? s.Items.Sum(i => i.LineTotal) : s.Total;

        var report = new SalesReport
        {
            From = fromUtc,
            To = toUtc,
            SalesCount = completed.Count,
            Total = completed.Sum(TotalOf),
            Subtotal = completed.Sum(s => categoryId.HasValue ? s.Items.Sum(i => i.LineTotal) : s.Subtotal),
            Discounts = categoryId.HasValue ? 0 : completed.Sum(s => s.DiscountAmount),
            Tax = categoryId.HasValue ? 0 : completed.Sum(s => s.TaxAmount),
            UnitsSold = completed.SelectMany(s => s.Items).Sum(i => i.Quantity),
            CancelledCount = cancelled.Count,
            CancelledTotal = cancelled.Sum(TotalOf)
        };

        report.ByPaymentMethod = completed
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new GroupRow(EscPosPrinter.PaymentLabel(g.Key), g.Sum(TotalOf), g.Count()))
            .OrderByDescending(g => g.Amount)
            .ToList();

        report.ByUser = completed
            .GroupBy(s => s.UserName)
            .Select(g => new GroupRow(g.Key, g.Sum(TotalOf), g.Count()))
            .OrderByDescending(g => g.Amount)
            .ToList();

        report.ByCategory = completed
            .SelectMany(s => s.Items)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.CategoryName) ? "Sin categoria" : i.CategoryName)
            .Select(g => new GroupRow(g.Key, g.Sum(i => i.LineTotal), g.Select(i => i.SaleId).Distinct().Count(), g.Sum(i => i.Quantity)))
            .OrderByDescending(g => g.Amount)
            .ToList();

        report.ByDay = completed
            .GroupBy(s => s.SoldAt.ToLocalTime().Date)
            .Select(g => new GroupRow(g.Key.ToString("dd/MM/yyyy"), g.Sum(TotalOf), g.Count()))
            .OrderBy(g => g.Label)
            .ToList();

        report.ByHour = completed
            .GroupBy(s => s.SoldAt.ToLocalTime().Hour)
            .Select(g => new GroupRow($"{g.Key:D2}:00", g.Sum(TotalOf), g.Count()))
            .OrderBy(g => g.Label)
            .ToList();

        report.TopProducts = CashSessionService.BuildProductRows(completed).Take(25).ToList();

        return report;
    }

    public async Task<ProductionReport> BuildProductionReportAsync(
        DateTime fromUtc, DateTime toUtc, int? categoryId, bool onlyProduced, CancellationToken ct = default)
    {
        var items = await db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale!.SoldAt >= fromUtc && i.Sale.SoldAt < toUtc &&
                        i.Sale.Status == SaleStatus.Completed)
            .AsNoTracking()
            .ToListAsync(ct);

        var todayStart = DateTime.UtcNow.Date;
        var days = Math.Max(1, (int)Math.Ceiling((toUtc - fromUtc).TotalDays));

        var productsQuery = db.Products.Include(p => p.Category).Where(p => p.IsActive);
        if (categoryId.HasValue) productsQuery = productsQuery.Where(p => p.CategoryId == categoryId.Value);
        if (onlyProduced) productsQuery = productsQuery.Where(p => p.IsProduced);

        var products = await productsQuery.AsNoTracking().ToListAsync(ct);

        var rows = new List<ProductionRow>();
        foreach (var product in products)
        {
            var productItems = items.Where(i => i.ProductId == product.Id).ToList();
            var soldInPeriod = productItems.Sum(i => i.Quantity);
            var soldToday = productItems.Where(i => i.Sale!.SoldAt >= todayStart).Sum(i => i.Quantity);

            // Solo se listan productos que se movieron o que estan por debajo del minimo.
            if (soldInPeriod == 0 && product.Stock > product.MinStock) continue;

            rows.Add(new ProductionRow
            {
                ProductId = product.Id,
                Code = product.Code,
                Name = product.Name,
                CategoryName = product.Category?.Name ?? string.Empty,
                Unit = product.Unit,
                SoldInPeriod = soldInPeriod,
                SoldToday = soldToday,
                AveragePerDay = Math.Round(soldInPeriod / days, 2),
                CurrentStock = product.Stock,
                MinStock = product.MinStock
            });
        }

        return new ProductionReport
        {
            From = fromUtc,
            To = toUtc,
            Days = days,
            Rows = rows
                .OrderByDescending(r => r.SuggestedOrder)
                .ThenByDescending(r => r.SoldInPeriod)
                .ToList()
        };
    }
}
