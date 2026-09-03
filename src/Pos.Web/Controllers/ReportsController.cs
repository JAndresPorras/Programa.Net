using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Security;
using Pos.Web.Services;

namespace Pos.Web.Controllers;

/// <summary>
/// Reportes de ventas y el pedido a la planta de producción, que se arma
/// con lo que realmente se vendió en el período.
/// </summary>
public class ReportsController(
    ApplicationDbContext db,
    IReportService reports,
    ISettingsService settings) : Controller
{
    [HttpGet]
    [HasPermission(Permissions.ReportsSales)]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, int? userId, int? categoryId)
    {
        var (start, end) = Range(from, to);
        var config = await settings.GetAsync();

        ViewData["Title"] = "Reporte de ventas";
        ViewData["Subtitle"] = $"Del {start:dd/MM/yyyy} al {end.AddDays(-1):dd/MM/yyyy}";
        ViewBag.Currency = config.Currency;
        ViewBag.From = start;
        ViewBag.To = end.AddDays(-1);
        ViewBag.UserId = userId;
        ViewBag.CategoryId = categoryId;
        ViewBag.Users = await db.Users.OrderBy(u => u.FullName)
            .Select(u => new SelectListItem(u.FullName, u.Id.ToString(), u.Id == userId))
            .ToListAsync();
        ViewBag.Categories = await db.Categories.OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == categoryId))
            .ToListAsync();

        var report = await reports.BuildSalesReportAsync(
            start.ToUniversalTime(), end.ToUniversalTime(), userId, categoryId);

        return View(report);
    }

    /// <summary>
    /// Listado de lo vendido para pedirle a la planta la cantidad faltante.
    /// La sugerencia sale de la demanda del período y del stock que queda.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.ReportsProduction)]
    public async Task<IActionResult> Production(DateTime? from, DateTime? to, int? categoryId,
                                                bool onlyProduced = true, bool print = false)
    {
        var (start, end) = Range(from, to);
        var config = await settings.GetAsync();

        ViewData["Title"] = "Pedido a la planta de producción";
        ViewData["Subtitle"] = $"Basado en las ventas del {start:dd/MM/yyyy} al {end.AddDays(-1):dd/MM/yyyy}";
        ViewBag.Currency = config.Currency;
        ViewBag.Settings = config;
        ViewBag.From = start;
        ViewBag.To = end.AddDays(-1);
        ViewBag.CategoryId = categoryId;
        ViewBag.OnlyProduced = onlyProduced;
        ViewBag.Print = print;
        ViewBag.Categories = await db.Categories.OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == categoryId))
            .ToListAsync();

        var report = await reports.BuildProductionReportAsync(
            start.ToUniversalTime(), end.ToUniversalTime(), categoryId, onlyProduced);

        return View(report);
    }

    [HttpGet]
    [HasPermission(Permissions.ReportsExport)]
    public async Task<IActionResult> ExportSales(DateTime? from, DateTime? to, int? userId, int? categoryId)
    {
        var (start, end) = Range(from, to);
        var report = await reports.BuildSalesReportAsync(
            start.ToUniversalTime(), end.ToUniversalTime(), userId, categoryId);

        var csv = new StringBuilder();
        csv.AppendLine("Codigo;Producto;Categoria;Unidad;Cantidad;Importe");
        foreach (var row in report.TopProducts)
        {
            csv.AppendLine(string.Join(';',
                Escape(row.Code), Escape(row.Name), Escape(row.CategoryName), Escape(row.Unit),
                row.Quantity.ToString(CultureInfo.InvariantCulture),
                row.Total.ToString(CultureInfo.InvariantCulture)));
        }
        csv.AppendLine();
        csv.AppendLine($"Total vendido;{report.Total.ToString(CultureInfo.InvariantCulture)}");
        csv.AppendLine($"Recibos;{report.SalesCount}");

        return CsvFile(csv, $"ventas_{start:yyyyMMdd}_{end.AddDays(-1):yyyyMMdd}.csv");
    }

    [HttpGet]
    [HasPermission(Permissions.ReportsExport)]
    public async Task<IActionResult> ExportProduction(DateTime? from, DateTime? to, int? categoryId,
                                                      bool onlyProduced = true)
    {
        var (start, end) = Range(from, to);
        var report = await reports.BuildProductionReportAsync(
            start.ToUniversalTime(), end.ToUniversalTime(), categoryId, onlyProduced);

        var csv = new StringBuilder();
        csv.AppendLine("Codigo;Producto;Categoria;Unidad;VendidoPeriodo;VendidoHoy;PromedioDia;StockActual;StockMinimo;CantidadAPedir");
        foreach (var row in report.Rows)
        {
            csv.AppendLine(string.Join(';',
                Escape(row.Code), Escape(row.Name), Escape(row.CategoryName), Escape(row.Unit),
                row.SoldInPeriod.ToString(CultureInfo.InvariantCulture),
                row.SoldToday.ToString(CultureInfo.InvariantCulture),
                row.AveragePerDay.ToString(CultureInfo.InvariantCulture),
                row.CurrentStock.ToString(CultureInfo.InvariantCulture),
                row.MinStock.ToString(CultureInfo.InvariantCulture),
                row.SuggestedOrder.ToString(CultureInfo.InvariantCulture)));
        }

        return CsvFile(csv, $"pedido_produccion_{DateTime.Now:yyyyMMdd}.csv");
    }

    /// <summary>Normaliza el rango de fechas locales que llega de los filtros.</summary>
    private static (DateTime Start, DateTime End) Range(DateTime? from, DateTime? to)
    {
        var start = (from ?? DateTime.Now).Date;
        var end = (to ?? DateTime.Now).Date.AddDays(1);
        if (end <= start) end = start.AddDays(1);
        return (start, end);
    }

    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace(';', ',').Replace("\n", " ").Trim();

    private FileContentResult CsvFile(StringBuilder csv, string fileName)
    {
        // El BOM hace que Excel abra el archivo con los acentos correctos.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
