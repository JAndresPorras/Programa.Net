using Pos.Web.Models;
using Pos.Web.Services;

namespace Pos.Web.ViewModels;

/// <summary>Estado del ticket que se devuelve al navegador tras cada escaneo.</summary>
public class CartStateDto
{
    public List<CartLineDto> Lines { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    /// <summary>Ajuste por redondeo al efectivo aplicado al total.</summary>
    public decimal Rounding { get; set; }
    public string RoundingText { get; set; } = string.Empty;

    public decimal Units { get; set; }
    public int LineCount { get; set; }
    public string SubtotalText { get; set; } = string.Empty;
    public string DiscountText { get; set; } = string.Empty;
    public string TotalText { get; set; } = string.Empty;
    public List<QuickAmountDto> QuickAmounts { get; set; } = [];

    /// <param name="trackedStock">
    /// Existencias digitales de los productos del ticket que estan en el inventario
    /// personalizado. Solo sirve para mostrarlas: nunca impide vender.
    /// </param>
    public static CartStateDto From(Cart cart, CurrencyInfo currency, bool roundTotalToCash,
                                    IReadOnlyDictionary<int, decimal>? trackedStock = null)
    {
        // Igual que al cerrar la venta: se suman las lineas ya redondeadas.
        var subtotal = currency.Round(cart.Lines.Sum(l => currency.Round(l.LineTotal)));
        var discount = currency.Round(cart.DiscountAmount);
        var total = currency.Round(subtotal - discount);
        var rounded = roundTotalToCash ? currency.RoundCashTotal(total) : total;

        return new CartStateDto
        {
            Lines = cart.Lines.Select(l => CartLineDto.From(l, currency,
                trackedStock is not null && trackedStock.TryGetValue(l.ProductId, out var stock) ? stock : null)).ToList(),
            Subtotal = subtotal,
            Discount = discount,
            Total = rounded,
            Rounding = currency.Round(rounded - total),
            RoundingText = currency.Format(currency.Round(rounded - total)),
            Units = cart.TotalItems,
            LineCount = cart.Lines.Count,
            SubtotalText = currency.Format(subtotal),
            DiscountText = currency.Format(discount),
            TotalText = currency.Format(rounded),
            QuickAmounts = currency.QuickAmounts(rounded)
                .Select(a => new QuickAmountDto { Value = a, Text = currency.Format(a) })
                .ToList()
        };
    }
}

public class QuickAmountDto
{
    public decimal Value { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class CartLineDto
{
    public int ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string Unit { get; set; } = "UND";
    public bool SoldByWeight { get; set; }
    public string? ImagePath { get; set; }
    public string UnitPriceText { get; set; } = string.Empty;
    public string LineTotalText { get; set; } = string.Empty;
    public string QuantityText { get; set; } = string.Empty;

    /// <summary>El producto esta en el inventario personalizado.</summary>
    public bool TrackInventory { get; set; }

    /// <summary>Existencia digital (solo si TrackInventory).</summary>
    public decimal? Stock { get; set; }

    public string? StockText { get; set; }

    /// <summary>Lo que hay en el ticket supera el inventario digital. Solo informa.</summary>
    public bool StockShort { get; set; }

    public static CartLineDto From(CartLine l, CurrencyInfo currency, decimal? trackedStock = null) => new()
    {
        TrackInventory = trackedStock.HasValue,
        Stock = trackedStock,
        StockText = trackedStock.HasValue
            ? (l.SoldByWeight
                ? trackedStock.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                : EscPosPrinter.FormatQuantity(trackedStock.Value))
            : null,
        StockShort = trackedStock.HasValue && l.Quantity > trackedStock.Value,
        ProductId = l.ProductId,
        Code = l.Code,
        Name = l.Name,
        CategoryName = l.CategoryName,
        UnitPrice = l.UnitPrice,
        Quantity = l.Quantity,
        LineTotal = currency.Round(l.LineTotal),
        Unit = l.Unit,
        SoldByWeight = l.SoldByWeight,
        ImagePath = l.ImagePath,
        UnitPriceText = currency.Format(l.UnitPrice),
        LineTotalText = currency.Format(l.LineTotal),
        QuantityText = l.SoldByWeight
            ? l.Quantity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            : EscPosPrinter.FormatQuantity(l.Quantity)
    };
}

/// <summary>Respuesta estandar de las llamadas del punto de venta.</summary>
public class PosResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? MessageKind { get; set; }
    public CartStateDto? Cart { get; set; }
    public object? Data { get; set; }

    public static PosResponse Ok(CartStateDto cart, string? message = null) =>
        new() { Success = true, Cart = cart, Message = message, MessageKind = "success" };

    public static PosResponse Fail(string message) =>
        new() { Success = false, Message = message, MessageKind = "danger" };
}

public class PosIndexViewModel
{
    public CartStateDto Cart { get; set; } = new();
    public CurrencyInfo Currency { get; set; } = CurrencyInfo.Pen;
    public CashSession? OpenSession { get; set; }
    public bool CanDiscount { get; set; }
    public bool StockControl { get; set; }
    public List<Category> Categories { get; set; } = [];
    public decimal DefaultOpeningFloat { get; set; }
}

/// <summary>
/// Lectura de la pistola. La cantidad viaja vacia cuando la cajera solo disparo
/// el codigo: si el producto se vende por peso, el POS le pide los kilos.
/// </summary>
public record ScanRequest(string? Barcode, decimal? Quantity = null);
public record QuantityRequest(int ProductId, decimal Quantity);
public record ProductRequest(int ProductId, decimal Quantity = 1);
public record DiscountRequest(decimal Amount);

public record CheckoutApiRequest(
    decimal AmountReceived,
    decimal Discount,
    string PaymentMethod,
    string? CustomerName,
    string? CustomerDocument,
    string? Notes,
    bool ConfirmStockShortage = false);
