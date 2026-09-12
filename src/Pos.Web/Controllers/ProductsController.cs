using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;
using Pos.Web.Security;
using Pos.Web.Services;
using Pos.Web.ViewModels;

namespace Pos.Web.Controllers;

/// <summary>
/// Maestro de códigos de barras: cada producto guarda categoría, código,
/// nombre, precio, imagen opcional y su historial de cambios de precio.
/// </summary>
[HasPermission(Permissions.ProductsView)]
public class ProductsController(
    ApplicationDbContext db,
    ISettingsService settings,
    IAuditService audit,
    IWebHostEnvironment environment) : Controller
{
    private const int PageSize = 25;

    [HttpGet]
    public async Task<IActionResult> Index(string? term, int? categoryId, bool onlyInactive = false,
                                           bool onlyLowStock = false, int page = 1)
    {
        var query = db.Products.Include(p => p.Category).Include(p => p.Barcodes).AsQueryable();

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{t}%") ||
                EF.Functions.Like(p.Code, $"%{t}%") ||
                p.Barcodes.Any(b => EF.Functions.Like(b.Code, $"%{t}%")));
        }

        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);
        query = onlyInactive ? query.Where(p => !p.IsActive) : query.Where(p => p.IsActive);
        if (onlyLowStock) query = query.Where(p => p.Stock <= p.MinStock);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        ViewData["Title"] = "Maestro de códigos de barras";
        ViewBag.Currency = (await settings.GetAsync()).Currency;

        return View(new ProductIndexViewModel
        {
            Products = await query.OrderBy(p => p.Name)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .AsNoTracking().ToListAsync(),
            Categories = await db.Categories.OrderBy(c => c.Name).AsNoTracking().ToListAsync(),
            Term = term,
            CategoryId = categoryId,
            OnlyInactive = onlyInactive,
            OnlyLowStock = onlyLowStock,
            Page = page,
            TotalPages = totalPages,
            TotalCount = total
        });
    }

    [HttpGet]
    [HasPermission(Permissions.ProductsCreate)]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Nuevo producto";
        return View("Form", new ProductFormViewModel
        {
            Categories = await CategoriesAsync(),
            Unit = "UND",
            IsActive = true,
            IsProduced = true,
            CanChangePrice = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.ProductsCreate)]
    public async Task<IActionResult> Create(ProductFormViewModel model)
    {
        ViewData["Title"] = "Nuevo producto";
        model.Categories = await CategoriesAsync();

        if (await db.Products.AnyAsync(p => p.Code == model.Code.Trim()))
        {
            ModelState.AddModelError(nameof(model.Code), "Ya existe un producto con ese código.");
        }

        var barcodes = ParseBarcodes(model.BarcodesRaw);
        if (barcodes.Count == 0)
        {
            ModelState.AddModelError(nameof(model.BarcodesRaw),
                "Registre al menos un código de barras para poder escanear el producto.");
        }

        await ValidateCashPriceAsync(model.Price);

        var taken = await db.Barcodes.Where(b => barcodes.Contains(b.Code)).Select(b => b.Code).ToListAsync();
        if (taken.Count > 0)
        {
            ModelState.AddModelError(nameof(model.BarcodesRaw),
                $"Estos códigos ya están asignados a otro producto: {string.Join(", ", taken)}");
        }

        if (!ModelState.IsValid) return View("Form", model);

        var product = new Product
        {
            Code = model.Code.Trim(),
            Name = model.Name.Trim(),
            Description = model.Description,
            CategoryId = model.CategoryId,
            Price = model.Price,
            Cost = model.Cost,
            Stock = model.Stock,
            MinStock = model.MinStock,
            Unit = model.Unit.Trim().ToUpperInvariant(),
            SoldByWeight = model.SoldByWeight,
            IsProduced = model.IsProduced,
            IsActive = model.IsActive,
            CreatedByUserId = User.GetUserId(),
            ImagePath = await SaveImageAsync(model.ImageFile)
        };

        foreach (var code in barcodes)
        {
            product.Barcodes.Add(new Barcode
            {
                Code = code,
                IsPrimary = product.Barcodes.Count == 0,
                Symbology = code.Length == 13 ? "EAN13" : "CODE128",
                CreatedByUserId = User.GetUserId()
            });
        }

        db.Products.Add(product);
        await db.SaveChangesAsync();

        // El precio inicial también queda registrado en el historial.
        db.PriceHistories.Add(new PriceHistory
        {
            ProductId = product.Id,
            OldPrice = 0,
            NewPrice = product.Price,
            ChangedAt = DateTime.UtcNow,
            ChangedByUserId = User.GetUserId(),
            ChangedByUserName = User.GetDisplayName(),
            Reason = "Precio inicial del producto"
        });
        await db.SaveChangesAsync();

        await audit.LogAsync("ProductoCreado", "Producto", product.Id.ToString(),
            $"{product.Code} - {product.Name}");
        TempData["Success"] = $"El producto {product.Name} fue creado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [HasPermission(Permissions.ProductsEdit)]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await db.Products
            .Include(p => p.Barcodes)
            .Include(p => p.PriceHistories)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();

        ViewData["Title"] = $"Editar {product.Name}";
        return View("Form", new ProductFormViewModel
        {
            Id = product.Id,
            Code = product.Code,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            Price = product.Price,
            OriginalPrice = product.Price,
            Cost = product.Cost,
            Stock = product.Stock,
            MinStock = product.MinStock,
            Unit = product.Unit,
            SoldByWeight = product.SoldByWeight,
            IsProduced = product.IsProduced,
            IsActive = product.IsActive,
            ImagePath = product.ImagePath,
            BarcodesRaw = string.Join("\n", product.Barcodes.OrderByDescending(b => b.IsPrimary).Select(b => b.Code)),
            Categories = await CategoriesAsync(),
            CanChangePrice = User.HasPermission(Permissions.ProductsChangePrice),
            PriceHistory = product.PriceHistories.OrderByDescending(h => h.ChangedAt).Take(20).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.ProductsEdit)]
    public async Task<IActionResult> Edit(ProductFormViewModel model)
    {
        var product = await db.Products
            .Include(p => p.Barcodes)
            .Include(p => p.PriceHistories)
            .FirstOrDefaultAsync(p => p.Id == model.Id);

        if (product is null) return NotFound();

        ViewData["Title"] = $"Editar {product.Name}";
        model.Categories = await CategoriesAsync();
        model.OriginalPrice = product.Price;
        model.ImagePath = product.ImagePath;
        model.CanChangePrice = User.HasPermission(Permissions.ProductsChangePrice);
        model.PriceHistory = product.PriceHistories.OrderByDescending(h => h.ChangedAt).Take(20).ToList();

        if (await db.Products.AnyAsync(p => p.Code == model.Code.Trim() && p.Id != product.Id))
        {
            ModelState.AddModelError(nameof(model.Code), "Ya existe otro producto con ese código.");
        }

        var barcodes = ParseBarcodes(model.BarcodesRaw);
        if (barcodes.Count == 0)
        {
            ModelState.AddModelError(nameof(model.BarcodesRaw), "Registre al menos un código de barras.");
        }

        var taken = await db.Barcodes
            .Where(b => barcodes.Contains(b.Code) && b.ProductId != product.Id)
            .Select(b => b.Code)
            .ToListAsync();
        if (taken.Count > 0)
        {
            ModelState.AddModelError(nameof(model.BarcodesRaw),
                $"Estos códigos ya están asignados a otro producto: {string.Join(", ", taken)}");
        }

        // Sin el permiso de cambiar precios el campo se ignora y se conserva el actual.
        if (!model.CanChangePrice) model.Price = product.Price;

        // Solo se exige el múltiplo de moneda cuando el precio realmente cambia, para
        // no bloquear la edición de un producto viejo cargado con otra configuración.
        if (model.Price != product.Price) await ValidateCashPriceAsync(model.Price);

        if (!ModelState.IsValid) return View("Form", model);

        var priceChanged = product.Price != model.Price;
        var oldPrice = product.Price;

        product.Code = model.Code.Trim();
        product.Name = model.Name.Trim();
        product.Description = model.Description;
        product.CategoryId = model.CategoryId;
        product.Price = model.Price;
        product.Cost = model.Cost;
        product.Stock = model.Stock;
        product.MinStock = model.MinStock;
        product.Unit = model.Unit.Trim().ToUpperInvariant();
        product.SoldByWeight = model.SoldByWeight;
        product.IsProduced = model.IsProduced;
        product.IsActive = model.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        product.UpdatedByUserId = User.GetUserId();

        if (model.RemoveImage)
        {
            DeleteImage(product.ImagePath);
            product.ImagePath = null;
        }

        var newImage = await SaveImageAsync(model.ImageFile);
        if (newImage is not null)
        {
            DeleteImage(product.ImagePath);
            product.ImagePath = newImage;
        }

        // Se sincronizan los códigos de barras con lo que quedó en el formulario.
        foreach (var existing in product.Barcodes.Where(b => !barcodes.Contains(b.Code)).ToList())
        {
            db.Barcodes.Remove(existing);
            product.Barcodes.Remove(existing);
        }
        foreach (var code in barcodes.Where(code => product.Barcodes.All(b => b.Code != code)))
        {
            product.Barcodes.Add(new Barcode
            {
                Code = code,
                Symbology = code.Length == 13 ? "EAN13" : "CODE128",
                CreatedByUserId = User.GetUserId()
            });
        }
        var primary = product.Barcodes.FirstOrDefault(b => b.Code == barcodes[0]);
        foreach (var b in product.Barcodes) b.IsPrimary = b == primary;

        // Todo cambio de precio queda registrado con fecha, hora y usuario.
        if (priceChanged)
        {
            db.PriceHistories.Add(new PriceHistory
            {
                ProductId = product.Id,
                OldPrice = oldPrice,
                NewPrice = product.Price,
                ChangedAt = DateTime.UtcNow,
                ChangedByUserId = User.GetUserId(),
                ChangedByUserName = User.GetDisplayName(),
                Reason = model.PriceChangeReason
            });
        }

        await db.SaveChangesAsync();

        await audit.LogAsync("ProductoEditado", "Producto", product.Id.ToString(),
            priceChanged
                ? $"{product.Code}: precio {oldPrice} -> {product.Price}"
                : $"{product.Code} - {product.Name}");

        TempData["Success"] = priceChanged
            ? $"{product.Name} actualizado. El cambio de precio quedó en el historial."
            : $"{product.Name} actualizado.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Historial completo de cambios de precio de un producto.</summary>
    [HttpGet]
    public async Task<IActionResult> History(int id)
    {
        var product = await db.Products
            .Include(p => p.Category)
            .Include(p => p.PriceHistories.OrderByDescending(h => h.ChangedAt))
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null) return NotFound();

        ViewData["Title"] = $"Historial de precios · {product.Name}";
        ViewBag.Currency = (await settings.GetAsync()).Currency;
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [HasPermission(Permissions.ProductsDelete)]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        product.IsActive = !product.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        product.UpdatedByUserId = User.GetUserId();
        await db.SaveChangesAsync();

        await audit.LogAsync(product.IsActive ? "ProductoActivado" : "ProductoDesactivado",
            "Producto", product.Id.ToString(), product.Name);

        TempData["Success"] = product.IsActive
            ? $"{product.Name} fue activado."
            : $"{product.Name} fue desactivado y ya no se puede vender.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Comprueba que el precio que paga el cliente se pueda cobrar con las monedas
    /// que existen. En Peru no hay monedas de 1 ni 5 centimos, asi que un precio de
    /// S/ 4.17 obligaria a perder centimos en cada venta.
    /// </summary>
    private async Task ValidateCashPriceAsync(decimal price)
    {
        var config = await settings.GetAsync();
        if (!config.PriceCashSteps) return;

        var currency = config.Currency;
        var publicPrice = config.PublicPrice(price);
        if (currency.IsCashAmount(publicPrice)) return;

        var down = currency.FloorCash(publicPrice);
        var up = currency.CeilCash(publicPrice);
        var label = config.TaxAddedOnTop ? "El precio al público" : "El precio";

        ModelState.AddModelError(nameof(ProductFormViewModel.Price),
            $"{label} queda en {currency.Format(publicPrice)} y no se puede pagar exacto: " +
            $"en {currency.Name} la moneda más pequeña es {currency.Format(currency.CashRounding)}. " +
            $"Baje {currency.Format(currency.DownTo(publicPrice))} para llegar a {currency.Format(down)} " +
            $"o suba {currency.Format(currency.UpTo(publicPrice))} para llegar a {currency.Format(up)}.");
    }

    private async Task<List<Category>> CategoriesAsync() =>
        await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).AsNoTracking().ToListAsync();

    /// <summary>Separa los códigos escritos por coma, punto y coma, espacio o salto de línea.</summary>
    internal static List<string> ParseBarcodes(string? raw) =>
        (raw ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length is > 0 and <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static readonly string[] AllowedImageTypes = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private async Task<string?> SaveImageAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > 3 * 1024 * 1024)
        {
            ModelState.AddModelError("ImageFile", "La imagen no puede pesar más de 3 MB.");
            return null;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageTypes.Contains(extension))
        {
            ModelState.AddModelError("ImageFile", "Formato no permitido. Use JPG, PNG, WEBP o GIF.");
            return null;
        }

        var folder = Path.Combine(environment.WebRootPath, "uploads", "products");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var stream = System.IO.File.Create(Path.Combine(folder, fileName));
        await file.CopyToAsync(stream);

        return $"/uploads/products/{fileName}";
    }

    private void DeleteImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        try
        {
            var full = Path.Combine(environment.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
        }
        catch (IOException)
        {
            // Si el archivo está bloqueado no vale la pena interrumpir el guardado.
        }
    }
}
