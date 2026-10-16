using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;
using Pos.Web.ViewModels;

namespace Pos.Web.Controllers;

/// <summary>
/// Inventario personalizado: el usuario elige que productos de las categorias
/// habilitadas (Yogures y Quesos) quiere controlar, registra entradas o corrige
/// cantidades, y las ventas los descuentan solas sin bloquear nunca la caja.
/// </summary>
[HasPermission(Permissions.InventoryManage)]
public class InventoryController(
    ApplicationDbContext db,
    IInventoryService inventory,
    ISettingsService settings,
    IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Inventario";
        return View(new InventoryIndexViewModel
        {
            Rows = await inventory.TrackedRowsAsync(),
            EligibleCategories = await inventory.EligibleCategoriesAsync(),
            CanManageCategories = User.HasPermission(Permissions.CategoriesManage)
        });
    }

    /// <summary>Productos de las categorias habilitadas que se pueden agregar.</summary>
    [HttpGet]
    public async Task<IActionResult> Search(string? term)
    {
        var query = db.Products
            .Include(p => p.Category).ThenInclude(c => c!.ParentCategory)
            .Include(p => p.Barcodes)
            .Where(p => p.IsActive && p.Category!.InventoryEnabled && p.Category.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{t}%") ||
                EF.Functions.Like(p.Code, $"%{t}%") ||
                p.Barcodes.Any(b => b.Code == t));
        }

        var products = await query.OrderBy(p => p.Name).Take(30).AsNoTracking().ToListAsync();

        return Json(products.Select(p => new
        {
            productId = p.Id,
            code = p.Code,
            name = p.Name,
            categoryName = p.Category!.FullName,
            unit = p.Unit,
            tracked = p.TrackInventory,
            stockText = InventoryText.Quantity(p.Stock, p.SoldByWeight)
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Track([FromBody] InventoryProductRequest request)
    {
        var result = await inventory.TrackAsync(request.ProductId);
        if (!result.Success) return Json(new { success = false, message = result.Errors[0] });

        var name = await ProductNameAsync(request.ProductId);
        await audit.LogAsync("InventarioAgregar", "Producto", request.ProductId.ToString(), name);
        return Json(new { success = true, message = $"{name} agregado al inventario.", rows = await RowsJsonAsync() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Untrack([FromBody] InventoryProductRequest request)
    {
        var result = await inventory.UntrackAsync(request.ProductId);
        if (!result.Success) return Json(new { success = false, message = result.Errors[0] });

        var name = await ProductNameAsync(request.ProductId);
        await audit.LogAsync("InventarioQuitar", "Producto", request.ProductId.ToString(), name);
        return Json(new
        {
            success = true,
            message = $"{name} salió del inventario. Las ventas ya no lo descuentan.",
            rows = await RowsJsonAsync()
        });
    }

    /// <summary>Guarda las cantidades escritas: entrada (suma) o edicion (reemplaza).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply([FromBody] InventoryApplyRequest request)
    {
        InventoryMode mode;
        if (string.Equals(request.Mode, "entry", StringComparison.OrdinalIgnoreCase)) mode = InventoryMode.Entry;
        else if (string.Equals(request.Mode, "set", StringComparison.OrdinalIgnoreCase)) mode = InventoryMode.Set;
        else return Json(new { success = false, errors = new[] { "Elija si es una entrada o una edición de cantidad." } });

        var lines = (request.Items ?? []).Select(i => new InventoryLine(i.ProductId, i.Quantity)).ToList();
        var result = await inventory.ApplyAsync(mode, lines, User.GetUserId(), User.GetDisplayName(), request.Note);

        if (!result.Success) return Json(new { success = false, errors = result.Errors });

        await audit.LogAsync(mode == InventoryMode.Entry ? "InventarioEntrada" : "InventarioEdicion",
            "Inventario", null, $"{lines.Count} productos");

        return Json(new
        {
            success = true,
            message = mode == InventoryMode.Entry
                ? $"Entrada registrada en {lines.Count} {(lines.Count == 1 ? "producto" : "productos")}."
                : $"Cantidad actualizada en {lines.Count} {(lines.Count == 1 ? "producto" : "productos")}.",
            rows = InventoryJson.Rows(result.Rows)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Movements(int? productId)
    {
        var query = db.InventoryMovements.Include(m => m.Product).AsQueryable();
        if (productId.HasValue) query = query.Where(m => m.ProductId == productId.Value);

        var list = await query.OrderByDescending(m => m.Id).Take(60).AsNoTracking().ToListAsync();
        return Json(list.Select(m => new
        {
            id = m.Id,
            productId = m.ProductId,
            productName = m.Product?.Name ?? string.Empty,
            type = m.Type.ToString(),
            typeLabel = InventoryText.MovementLabel(m.Type),
            quantityText = (m.Quantity > 0 ? "+" : "") + InventoryText.Quantity(m.Quantity, m.Product?.SoldByWeight == true),
            beforeText = InventoryText.Quantity(m.StockBefore, m.Product?.SoldByWeight == true),
            afterText = InventoryText.Quantity(m.StockAfter, m.Product?.SoldByWeight == true),
            unit = m.Product?.Unit ?? string.Empty,
            when = m.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            userName = m.UserName,
            note = m.Note,
            saleId = m.SaleId
        }));
    }

    /// <summary>Descarga el inventario y sus movimientos en un libro de Excel.</summary>
    [HttpGet]
    public async Task<IActionResult> Excel()
    {
        var config = await settings.GetAsync();
        var rows = await inventory.TrackedRowsAsync();
        var since = DateTime.UtcNow.AddDays(-90);
        var ids = rows.Select(r => r.ProductId).ToList();
        var movements = await db.InventoryMovements
            .Include(m => m.Product)
            .Where(m => m.CreatedAt >= since && ids.Contains(m.ProductId))
            .OrderByDescending(m => m.Id)
            .AsNoTracking()
            .ToListAsync();

        using var book = new XLWorkbook();

        // ------------------------------------------------------ Hoja inventario
        var sheet = book.Worksheets.Add("Inventario");
        sheet.Cell(1, 1).Value = $"Inventario · {config.BusinessName}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = $"Generado el {DateTime.Now:dd/MM/yyyy HH:mm} por {User.GetDisplayName()}";
        sheet.Cell(2, 1).Style.Font.SetFontColor(XLColor.Gray);

        string[] headers = ["Código", "Producto", "Categoría", "Unidad", "Cantidad en sistema",
                            "Stock mínimo", "Estado", "Última actualización", "Actualizado por"];
        const int headerRow = 4;
        for (var c = 0; c < headers.Length; c++) sheet.Cell(headerRow, c + 1).Value = headers[c];

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.Code;
            sheet.Cell(r, 2).Value = row.Name;
            sheet.Cell(r, 3).Value = row.CategoryName;
            sheet.Cell(r, 4).Value = row.Unit;
            sheet.Cell(r, 5).Value = row.Stock;
            sheet.Cell(r, 6).Value = row.MinStock;
            sheet.Cell(r, 7).Value = row.IsEmpty ? "Sin existencias" : row.IsLow ? "Bajo el mínimo" : "Disponible";
            if (row.LastManualAt.HasValue)
            {
                sheet.Cell(r, 8).Value = row.LastManualAt.Value.ToLocalTime();
                sheet.Cell(r, 8).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            }
            sheet.Cell(r, 9).Value = row.LastManualBy ?? string.Empty;

            var format = row.SoldByWeight ? "0.000" : "0";
            sheet.Cell(r, 5).Style.NumberFormat.Format = format;
            sheet.Cell(r, 6).Style.NumberFormat.Format = format;
            if (row.IsEmpty) sheet.Cell(r, 7).Style.Font.SetFontColor(XLColor.FromHtml("#B91C1C"));
            else if (row.IsLow) sheet.Cell(r, 7).Style.Font.SetFontColor(XLColor.FromHtml("#B45309"));
            r++;
        }

        if (rows.Count > 0)
        {
            var table = sheet.Range(headerRow, 1, r - 1, headers.Length).CreateTable("Inventario");
            table.Theme = XLTableTheme.TableStyleLight9;
        }
        else
        {
            sheet.Cell(headerRow, 1).Value = "Todavía no hay productos en el inventario.";
        }

        double[] widths = [12, 34, 24, 9, 20, 14, 16, 20, 22];
        for (var c = 0; c < widths.Length; c++) sheet.Column(c + 1).Width = widths[c];
        sheet.SheetView.FreezeRows(headerRow);

        // ---------------------------------------------------- Hoja movimientos
        var moves = book.Worksheets.Add("Movimientos");
        moves.Cell(1, 1).Value = "Movimientos de los últimos 90 días";
        moves.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        string[] moveHeaders = ["Fecha", "Producto", "Movimiento", "Cambio", "Antes", "Después", "Unidad", "Usuario", "Detalle"];
        const int moveHeaderRow = 3;
        for (var c = 0; c < moveHeaders.Length; c++) moves.Cell(moveHeaderRow, c + 1).Value = moveHeaders[c];

        var m = moveHeaderRow + 1;
        foreach (var mv in movements)
        {
            var byWeight = mv.Product?.SoldByWeight == true;
            var format = byWeight ? "+0.000;-0.000;0" : "+0;-0;0";
            moves.Cell(m, 1).Value = mv.CreatedAt.ToLocalTime();
            moves.Cell(m, 1).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
            moves.Cell(m, 2).Value = mv.Product?.Name ?? string.Empty;
            moves.Cell(m, 3).Value = InventoryText.MovementLabel(mv.Type);
            moves.Cell(m, 4).Value = mv.Quantity;
            moves.Cell(m, 4).Style.NumberFormat.Format = format;
            moves.Cell(m, 5).Value = mv.StockBefore;
            moves.Cell(m, 6).Value = mv.StockAfter;
            moves.Cell(m, 5).Style.NumberFormat.Format = byWeight ? "0.000" : "0";
            moves.Cell(m, 6).Style.NumberFormat.Format = byWeight ? "0.000" : "0";
            moves.Cell(m, 7).Value = mv.Product?.Unit ?? string.Empty;
            moves.Cell(m, 8).Value = mv.UserName;
            moves.Cell(m, 9).Value = mv.Note ?? string.Empty;
            m++;
        }

        if (movements.Count > 0)
        {
            var table = moves.Range(moveHeaderRow, 1, m - 1, moveHeaders.Length).CreateTable("Movimientos");
            table.Theme = XLTableTheme.TableStyleLight9;
        }
        else
        {
            moves.Cell(moveHeaderRow, 1).Value = "Sin movimientos en los últimos 90 días.";
        }

        double[] moveWidths = [17, 30, 26, 10, 10, 10, 8, 22, 44];
        for (var c = 0; c < moveWidths.Length; c++) moves.Column(c + 1).Width = moveWidths[c];
        moves.SheetView.FreezeRows(moveHeaderRow);

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        await audit.LogAsync("InventarioExcel", "Inventario", null, $"{rows.Count} productos");

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"inventario_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    private async Task<string> ProductNameAsync(int id) =>
        await db.Products.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync() ?? $"#{id}";

    private async Task<object> RowsJsonAsync() => InventoryJson.Rows(await inventory.TrackedRowsAsync());

}
