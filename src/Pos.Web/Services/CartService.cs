using System.Text.Json;
using Pos.Web.Models;

namespace Pos.Web.Services;

/// <summary>Una linea del ticket en curso.</summary>
public class CartLine
{
    public int ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? ScannedBarcode { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal LineDiscount { get; set; }
    public string Unit { get; set; } = "UND";
    public bool SoldByWeight { get; set; }
    public string? ImagePath { get; set; }

    public decimal LineTotal => Math.Max(0, UnitPrice * Quantity - LineDiscount);
}

/// <summary>Ticket en curso guardado en la sesion de la cajera.</summary>
public class Cart
{
    public List<CartLine> Lines { get; set; } = [];
    public decimal DiscountAmount { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerDocument { get; set; }

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public decimal Total => Math.Max(0, Subtotal - DiscountAmount);
    public decimal TotalItems => Lines.Sum(l => l.Quantity);
    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Mantiene el ticket en curso en la sesion del navegador, de modo que si la
/// cajera recarga la pagina o navega a otro menu no pierde la venta.
/// </summary>
public interface ICartService
{
    Cart Get();
    void Save(Cart cart);
    void Clear();
    CartLine AddProduct(Product product, decimal quantity = 1, string? scannedBarcode = null);
    bool SetQuantity(int productId, decimal quantity);
    bool Remove(int productId);
}

public class CartService(IHttpContextAccessor accessor) : ICartService
{
    private const string SessionKey = "pos.cart";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

    private ISession Session =>
        accessor.HttpContext?.Session ??
        throw new InvalidOperationException("La sesion no esta disponible");

    public Cart Get()
    {
        var raw = Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(raw)) return new Cart();
        try
        {
            return JsonSerializer.Deserialize<Cart>(raw, JsonOptions) ?? new Cart();
        }
        catch (JsonException)
        {
            return new Cart();
        }
    }

    public void Save(Cart cart) =>
        Session.SetString(SessionKey, JsonSerializer.Serialize(cart, JsonOptions));

    public void Clear() => Session.Remove(SessionKey);

    public CartLine AddProduct(Product product, decimal quantity = 1, string? scannedBarcode = null)
    {
        var cart = Get();
        var line = cart.Lines.FirstOrDefault(l => l.ProductId == product.Id);

        if (line is null)
        {
            line = new CartLine
            {
                ProductId = product.Id,
                Code = product.Code,
                Name = product.Name,
                CategoryName = product.Category?.Name ?? string.Empty,
                ScannedBarcode = scannedBarcode,
                UnitPrice = product.Price,
                Quantity = quantity,
                Unit = product.Unit,
                SoldByWeight = product.SoldByWeight,
                ImagePath = product.ImagePath
            };
            cart.Lines.Add(line);
        }
        else
        {
            // Volver a escanear el mismo codigo suma una unidad mas.
            line.Quantity += quantity;
            line.UnitPrice = product.Price;
        }

        Save(cart);
        return line;
    }

    public bool SetQuantity(int productId, decimal quantity)
    {
        var cart = Get();
        var line = cart.Lines.FirstOrDefault(l => l.ProductId == productId);
        if (line is null) return false;

        if (quantity <= 0)
        {
            cart.Lines.Remove(line);
        }
        else
        {
            line.Quantity = line.SoldByWeight ? Math.Round(quantity, 3) : Math.Round(quantity, 0);
        }

        Save(cart);
        return true;
    }

    public bool Remove(int productId)
    {
        var cart = Get();
        var line = cart.Lines.FirstOrDefault(l => l.ProductId == productId);
        if (line is null) return false;
        cart.Lines.Remove(line);
        Save(cart);
        return true;
    }
}
