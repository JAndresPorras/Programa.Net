using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Web.Services;

public record CheckoutRequest(
    decimal AmountReceived,
    decimal DiscountAmount,
    PaymentMethod PaymentMethod,
    string? CustomerName,
    string? CustomerDocument,
    string? Notes);

public record CheckoutResult(bool Success, string? Error, Sale? Sale)
{
    public static CheckoutResult Fail(string error) => new(false, error, null);
    public static CheckoutResult Ok(Sale sale) => new(true, null, sale);
}

public interface ISaleService
{
    Task<CheckoutResult> CheckoutAsync(Cart cart, int userId, string userName, CheckoutRequest request, CancellationToken ct = default);
    Task<string> NextNumberAsync(string series, CancellationToken ct = default);
    Task<CheckoutResult> CancelAsync(int saleId, int userId, string userName, string reason, CancellationToken ct = default);
}

/// <summary>
/// Cierra la venta: guarda el recibo con copia de los datos del producto,
/// descuenta stock, la asocia al turno de caja abierto y devuelve el recibo
/// listo para imprimir.
/// </summary>
public class SaleService(
    ApplicationDbContext db,
    ISettingsService settings,
    ICashSessionService cashSessions,
    ILogger<SaleService> logger) : ISaleService
{
    public async Task<string> NextNumberAsync(string series, CancellationToken ct = default)
    {
        var prefix = string.IsNullOrWhiteSpace(series) ? "B001" : series.Trim().ToUpperInvariant();

        var last = await db.Sales
            .Where(s => s.Number.StartsWith(prefix))
            .OrderByDescending(s => s.Id)
            .Select(s => s.Number)
            .FirstOrDefaultAsync(ct);

        var correlative = 1;
        if (!string.IsNullOrEmpty(last))
        {
            var parts = last.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out var n)) correlative = n + 1;
        }

        return $"{prefix}-{correlative:D6}";
    }

    public async Task<CheckoutResult> CheckoutAsync(
        Cart cart, int userId, string userName, CheckoutRequest request, CancellationToken ct = default)
    {
        if (cart.IsEmpty) return CheckoutResult.Fail("No hay productos en el ticket.");

        var config = await settings.GetAsync(ct);
        var currency = config.Currency;

        // Se suman las lineas ya redondeadas para que el ticket impreso cuadre
        // exactamente con la suma que ve el cliente linea por linea.
        var subtotal = currency.Round(cart.Lines.Sum(l => currency.Round(l.LineTotal)));
        var discount = currency.Round(Math.Max(0, request.DiscountAmount));
        if (discount > subtotal) return CheckoutResult.Fail("El descuento no puede ser mayor que el subtotal.");

        var total = currency.Round(subtotal - discount);

        decimal tax = 0;
        if (config.TaxEnabled && config.TaxRate > 0)
        {
            tax = config.TaxIncluded
                ? currency.Round(total - total / (1 + config.TaxRate / 100m))
                : currency.Round(total * config.TaxRate / 100m);
            if (!config.TaxIncluded) total = currency.Round(total + tax);
        }

        // Un total de S/ 33.04 no se puede cobrar porque no existen monedas de
        // un centimo: se ajusta al multiplo de moneda mas cercano y el ajuste
        // queda registrado y se imprime en el recibo.
        decimal roundingAdjustment = 0m;
        if (config.TotalCashRounding)
        {
            var rounded = currency.RoundCashTotal(total);
            roundingAdjustment = currency.Round(rounded - total);
            total = rounded;
        }

        var received = currency.Round(request.AmountReceived);
        if (request.PaymentMethod == PaymentMethod.Cash && received < total)
        {
            return CheckoutResult.Fail(
                $"El dinero recibido ({currency.Format(received)}) es menor que el total ({currency.Format(total)}).");
        }

        // En pagos que no son efectivo no hay vuelto que devolver.
        var change = request.PaymentMethod == PaymentMethod.Cash
            ? currency.Round(received - total)
            : 0m;

        var session = await cashSessions.GetOpenSessionAsync(userId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var sale = new Sale
            {
                Number = await NextNumberAsync(config.ReceiptSeries, ct),
                CashSessionId = session?.Id,
                UserId = userId,
                UserName = userName,
                SoldAt = DateTime.UtcNow,
                Subtotal = subtotal,
                DiscountAmount = discount,
                TaxAmount = tax,
                RoundingAdjustment = roundingAdjustment,
                Total = total,
                AmountReceived = request.PaymentMethod == PaymentMethod.Cash ? received : total,
                ChangeGiven = change,
                PaymentMethod = request.PaymentMethod,
                CurrencyCode = currency.Code,
                Status = SaleStatus.Completed,
                CustomerName = request.CustomerName,
                CustomerDocument = request.CustomerDocument,
                Notes = request.Notes
            };

            foreach (var line in cart.Lines)
            {
                sale.Items.Add(new SaleItem
                {
                    ProductId = line.ProductId,
                    ScannedBarcode = line.ScannedBarcode,
                    ProductCode = line.Code,
                    ProductName = line.Name,
                    CategoryName = line.CategoryName,
                    UnitPrice = line.UnitPrice,
                    Quantity = line.Quantity,
                    LineDiscount = line.LineDiscount,
                    LineTotal = currency.Round(line.LineTotal),
                    Unit = line.Unit
                });
            }

            db.Sales.Add(sale);

            if (config.PosStockControl)
            {
                var ids = cart.Lines.Select(l => l.ProductId).ToList();
                var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
                foreach (var product in products)
                {
                    var sold = cart.Lines.Where(l => l.ProductId == product.Id).Sum(l => l.Quantity);
                    product.Stock -= sold;
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation("Venta {Number} cerrada por {User} por {Total}", sale.Number, userName, total);
            return CheckoutResult.Ok(sale);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(ex, "Error al cerrar la venta de {User}", userName);
            return CheckoutResult.Fail("No se pudo cerrar la venta. Intente nuevamente.");
        }
    }

    public async Task<CheckoutResult> CancelAsync(
        int saleId, int userId, string userName, string reason, CancellationToken ct = default)
    {
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleId, ct);
        if (sale is null) return CheckoutResult.Fail("El recibo no existe.");
        if (sale.Status == SaleStatus.Cancelled) return CheckoutResult.Fail("El recibo ya estaba anulado.");

        sale.Status = SaleStatus.Cancelled;
        sale.CancelledAt = DateTime.UtcNow;
        sale.CancelledByUserId = userId;
        sale.CancelReason = reason;

        var config = await settings.GetAsync(ct);
        if (config.PosStockControl)
        {
            var ids = sale.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).ToList();
            var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            foreach (var product in products)
            {
                var qty = sale.Items.Where(i => i.ProductId == product.Id).Sum(i => i.Quantity);
                product.Stock += qty;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogWarning("Venta {Number} anulada por {User}: {Reason}", sale.Number, userName, reason);
        return CheckoutResult.Ok(sale);
    }
}
