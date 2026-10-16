using Pos.Web.Models;
using Pos.Web.Services;

namespace Pos.Web.ViewModels;

public class InventoryIndexViewModel
{
    public List<InventoryRow> Rows { get; set; } = [];
    public List<Category> EligibleCategories { get; set; } = [];
    public bool CanManageCategories { get; set; }
}

public record InventoryProductRequest(int ProductId);

public record InventoryApplyItem(int ProductId, decimal Quantity);

/// <summary>Modo "entry" suma, modo "set" reemplaza.</summary>
public record InventoryApplyRequest(string? Mode, List<InventoryApplyItem>? Items, string? Note);

public static class InventoryText
{
    public static string MovementLabel(InventoryMovementType type) => type switch
    {
        InventoryMovementType.Entry => "Entrada",
        InventoryMovementType.Set => "Edición de cantidad",
        InventoryMovementType.Sale => "Venta",
        InventoryMovementType.SaleAdjustment => "Ajuste automático por venta",
        InventoryMovementType.SaleCancelled => "Anulación de venta",
        _ => type.ToString()
    };

    /// <summary>Unidades sin decimales y kilos con los tres decimales de la balanza.</summary>
    public static string Quantity(decimal value, bool byWeight) =>
        byWeight
            ? value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            : EscPosPrinter.FormatQuantity(value);
}

/// <summary>Forma en que viajan las filas del inventario al navegador.</summary>
public static class InventoryJson
{
    public static object Rows(IEnumerable<InventoryRow> rows) => rows.Select(r => new
    {
        productId = r.ProductId,
        code = r.Code,
        name = r.Name,
        categoryName = r.CategoryName,
        unit = r.Unit,
        soldByWeight = r.SoldByWeight,
        stock = r.Stock,
        stockText = InventoryText.Quantity(r.Stock, r.SoldByWeight),
        minStock = r.MinStock,
        isLow = r.IsLow,
        isEmpty = r.IsEmpty,
        isActive = r.IsActive,
        categoryEligible = r.CategoryEligible,
        lastManual = r.LastManualAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
        lastManualBy = r.LastManualBy
    }).ToList();
}
