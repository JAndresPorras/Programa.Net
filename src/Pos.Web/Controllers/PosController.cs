using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;
using Pos.Web.ViewModels;

namespace Pos.Web.Controllers;

/// <summary>
/// Pantalla principal del sistema: se escanea con la pistola, se arma el ticket,
/// se cobra, se calcula el vuelto y se imprime el recibo.
/// </summary>
[HasPermission(Permissions.PosSell)]
public class PosController(
    ApplicationDbContext db,
    ICartService cart,
    ISaleService sales,
    ICashSessionService cashSessions,
    ISettingsService settings,
    IAuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var config = await settings.GetAsync();
        var userId = User.GetUserId() ?? 0;

        ViewData["Title"] = "Punto de venta";
        ViewData["ContentClass"] = "content--flush";

        return View(new PosIndexViewModel
        {
            Cart = CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding),
            Currency = config.Currency,
            OpenSession = await cashSessions.GetOpenSessionAsync(userId),
            CanDiscount = User.HasPermission(Permissions.PosDiscount),
            StockControl = config.PosStockControl,
            DefaultOpeningFloat = config.PosDefaultOpeningFloat,
            Categories = await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync()
        });
    }

    /// <summary>Lectura de la pistola escaneadora: busca el código en el maestro y lo agrega.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan([FromBody] ScanRequest request)
    {
        var config = await settings.GetAsync();
        var code = (request.Barcode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            return Json(PosResponse.Fail("Escanee o escriba un código de barras."));
        }

        // La pistola envía el código tal cual está impreso en la etiqueta.
        var barcode = await db.Barcodes
            .Include(b => b.Product).ThenInclude(p => p!.Category)
            .FirstOrDefaultAsync(b => b.Code == code);

        var product = barcode?.Product;

        // Si no hay coincidencia exacta se intenta por código interno del producto.
        product ??= await db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Code == code);

        if (product is null)
        {
            return Json(PosResponse.Fail($"El código {code} no está registrado en el maestro de productos."));
        }

        if (!product.IsActive)
        {
            return Json(PosResponse.Fail($"{product.Name} está desactivado y no se puede vender."));
        }

        // Producto que se vende pesado y la cajera solo disparo el codigo:
        // el punto de venta le pide los kilos antes de agregarlo al ticket.
        if (product.SoldByWeight && request.Quantity is null)
        {
            return Json(new PosResponse
            {
                Success = true,
                Cart = CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding),
                Data = new
                {
                    needsWeight = true,
                    product = new
                    {
                        id = product.Id,
                        code = product.Code,
                        name = product.Name,
                        unit = product.Unit,
                        price = product.Price,
                        priceText = config.Currency.Format(product.Price),
                        inCart = cart.Get().Lines.FirstOrDefault(l => l.ProductId == product.Id)?.Quantity ?? 0
                    }
                }
            });
        }

        var quantity = request.Quantity is null or <= 0 ? 1m : request.Quantity.Value;

        if (config.PosStockControl)
        {
            var inCart = cart.Get().Lines.FirstOrDefault(l => l.ProductId == product.Id)?.Quantity ?? 0;
            if (inCart + quantity > product.Stock)
            {
                return Json(PosResponse.Fail(
                    $"No hay stock suficiente de {product.Name}. Disponible: {EscPosPrinter.FormatQuantity(product.Stock)}."));
            }
        }

        var line = cart.AddProduct(product, quantity, barcode?.Code ?? code);

        var state = CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding);
        return Json(new PosResponse
        {
            Success = true,
            Cart = state,
            Message = $"{product.Name} · {config.Currency.Format(product.Price)}",
            MessageKind = "success",
            Data = new { addedProductId = line.ProductId }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProduct([FromBody] ProductRequest request)
    {
        var config = await settings.GetAsync();
        var product = await db.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive);

        if (product is null) return Json(PosResponse.Fail("El producto no existe o está desactivado."));

        var line = cart.AddProduct(product, request.Quantity <= 0 ? 1 : request.Quantity);
        return Json(new PosResponse
        {
            Success = true,
            Cart = CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding),
            Message = $"{product.Name} agregado",
            MessageKind = "success",
            Data = new { addedProductId = line.ProductId }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetQuantity([FromBody] QuantityRequest request)
    {
        var config = await settings.GetAsync();
        if (!cart.SetQuantity(request.ProductId, request.Quantity))
        {
            return Json(PosResponse.Fail("La línea ya no está en el ticket."));
        }
        return Json(PosResponse.Ok(CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove([FromBody] ProductRequest request)
    {
        var config = await settings.GetAsync();
        cart.Remove(request.ProductId);
        return Json(PosResponse.Ok(CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        var config = await settings.GetAsync();
        cart.Clear();
        return Json(PosResponse.Ok(CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding), "Ticket vaciado"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.PosDiscount)]
    public async Task<IActionResult> SetDiscount([FromBody] DiscountRequest request)
    {
        var config = await settings.GetAsync();
        var current = cart.Get();
        var amount = Math.Max(0, request.Amount);

        if (amount > current.Subtotal)
        {
            return Json(PosResponse.Fail("El descuento no puede superar el subtotal del ticket."));
        }

        current.DiscountAmount = config.Currency.Round(amount);
        cart.Save(current);
        return Json(PosResponse.Ok(CartStateDto.From(current, config.Currency, config.TotalCashRounding), "Descuento aplicado"));
    }

    /// <summary>Búsqueda de productos para vender artículos sin código de barras a la mano.</summary>
    [HttpGet]
    public async Task<IActionResult> Search(string? term, int? categoryId)
    {
        var config = await settings.GetAsync();
        var query = db.Products.Include(p => p.Category).Include(p => p.Barcodes).Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{t}%") ||
                EF.Functions.Like(p.Code, $"%{t}%") ||
                p.Barcodes.Any(b => EF.Functions.Like(b.Code, $"%{t}%")));
        }

        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);

        var results = await query
            .OrderBy(p => p.Name)
            .Take(40)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                p.Unit,
                p.Stock,
                p.ImagePath,
                Category = p.Category!.Name,
                CategoryColor = p.Category.Color,
                Price = p.Price,
                Barcode = p.Barcodes.Where(b => b.IsPrimary).Select(b => b.Code).FirstOrDefault()
                          ?? p.Barcodes.Select(b => b.Code).FirstOrDefault()
            })
            .AsNoTracking()
            .ToListAsync();

        return Json(results.Select(r => new
        {
            r.Id, r.Code, r.Name, r.Unit, r.Stock, r.ImagePath, r.Category, r.CategoryColor, r.Barcode,
            Price = r.Price,
            PriceText = config.Currency.Format(r.Price)
        }));
    }

    /// <summary>Cierra la venta, guarda el recibo y devuelve la dirección para imprimirlo.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout([FromBody] CheckoutApiRequest request)
    {
        var config = await settings.GetAsync();
        var current = cart.Get();
        var userId = User.GetUserId() ?? 0;
        var userName = User.GetDisplayName();

        if (current.IsEmpty) return Json(PosResponse.Fail("No hay productos en el ticket."));

        var discount = User.HasPermission(Permissions.PosDiscount)
            ? Math.Max(0, request.Discount)
            : 0m;

        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var method))
        {
            method = PaymentMethod.Cash;
        }

        var result = await sales.CheckoutAsync(current, userId, userName, new CheckoutRequest(
            request.AmountReceived, discount, method,
            request.CustomerName, request.CustomerDocument, request.Notes));

        if (!result.Success || result.Sale is null)
        {
            return Json(PosResponse.Fail(result.Error ?? "No se pudo cerrar la venta."));
        }

        cart.Clear();
        await audit.LogAsync("VentaCerrada", "Venta", result.Sale.Id.ToString(),
            $"Recibo {result.Sale.Number} por {config.Currency.Format(result.Sale.Total)}");

        return Json(new PosResponse
        {
            Success = true,
            Cart = CartStateDto.From(cart.Get(), config.Currency, config.TotalCashRounding),
            Message = $"Venta {result.Sale.Number} cerrada",
            MessageKind = "success",
            Data = new
            {
                saleId = result.Sale.Id,
                number = result.Sale.Number,
                total = result.Sale.Total,
                totalText = config.Currency.Format(result.Sale.Total),
                receivedText = config.Currency.Format(result.Sale.AmountReceived),
                changeText = config.Currency.Format(result.Sale.ChangeGiven),
                change = result.Sale.ChangeGiven,
                receiptUrl = Url.Action("Receipt", "Sales", new { id = result.Sale.Id }),
                autoPrint = config.ReceiptPrintAuto,
                networkPrinter = string.Equals(config.PrinterMode, "network", StringComparison.OrdinalIgnoreCase)
            }
        });
    }

    /// <summary>Dinero recolectado en el turno. Es lo que revela el ojito del encabezado.</summary>
    [HttpGet]
    [HasPermission(Permissions.PosViewCollected)]
    public async Task<IActionResult> Recaudado()
    {
        var config = await settings.GetAsync();
        var userId = User.GetUserId() ?? 0;
        var amount = await cashSessions.GetCollectedAsync(userId);
        var session = await cashSessions.GetOpenSessionAsync(userId);

        return Json(new
        {
            amount,
            formatted = config.Currency.Format(amount),
            hasSession = session is not null,
            openedAt = session?.OpenedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        });
    }
}
