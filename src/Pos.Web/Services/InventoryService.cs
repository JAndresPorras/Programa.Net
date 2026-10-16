using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Web.Services;

/// <summary>Como se aplica la cantidad escrita en la vista de inventario.</summary>
public enum InventoryMode
{
    /// <summary>Entrada: suma a lo que hay (5 + 10 = 15).</summary>
    Entry,

    /// <summary>Editar cantidad: reemplaza por lo contado (5 -> 10).</summary>
    Set
}

public record InventoryLine(int ProductId, decimal Quantity);

/// <summary>Producto del inventario cuyas existencias digitales no alcanzan para lo que se vende.</summary>
public record StockShortage(int ProductId, string ProductName, string Unit, decimal Stock, decimal Quantity)
{
    /// <summary>Cuanto se sube automaticamente el inventario antes de registrar la salida.</summary>
    public decimal Missing => Quantity - Stock;
}

/// <summary>Fila de la vista de inventario.</summary>
public class InventoryRow
{
    public int ProductId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Unit { get; set; } = "UND";
    public bool SoldByWeight { get; set; }
    public bool IsActive { get; set; }
    public bool CategoryEligible { get; set; }
    public decimal Stock { get; set; }
    public decimal MinStock { get; set; }
    public DateTime? LastManualAt { get; set; }
    public string? LastManualBy { get; set; }

    public bool IsEmpty => Stock <= 0;
    public bool IsLow => !IsEmpty && Stock <= MinStock;
}

public class InventoryResult
{
    public bool Success => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<InventoryRow> Rows { get; set; } = [];

    public static InventoryResult Fail(string error)
    {
        var r = new InventoryResult();
        r.Errors.Add(error);
        return r;
    }
}

public interface IInventoryService
{
    /// <summary>Categorias cuyos productos se pueden agregar al inventario personalizado.</summary>
    Task<List<Category>> EligibleCategoriesAsync(CancellationToken ct = default);

    /// <summary>Productos elegidos, en el orden de la vista.</summary>
    Task<List<InventoryRow>> TrackedRowsAsync(CancellationToken ct = default);

    Task<InventoryResult> TrackAsync(int productId, CancellationToken ct = default);

    Task<InventoryResult> UntrackAsync(int productId, CancellationToken ct = default);

    /// <summary>
    /// Aplica entradas (suman) o ediciones (reemplazan). Es todo o nada: si una
    /// linea no es valida no se guarda ninguna, para no dejar el conteo a medias.
    /// </summary>
    Task<InventoryResult> ApplyAsync(InventoryMode mode, IReadOnlyList<InventoryLine> lines,
        int? userId, string userName, string? note, CancellationToken ct = default);

    /// <summary>Productos del inventario cuyas existencias digitales no alcanzan.</summary>
    Task<List<StockShortage>> FindShortagesAsync(IEnumerable<InventoryLine> lines, CancellationToken ct = default);

    /// <summary>
    /// Descuenta lo vendido de los productos del inventario. Si no alcanzaba,
    /// primero sube el inventario a lo vendido (ajuste automatico) y luego
    /// registra la salida, de modo que nunca queda negativo ni bloquea la venta.
    /// Debe llamarse dentro de la transaccion de la venta, con la venta ya guardada.
    /// </summary>
    Task RegisterSaleAsync(Sale sale, int? userId, string userName, CancellationToken ct = default);

    /// <summary>Devuelve al inventario lo que una venta anulada habia descontado.</summary>
    /// <returns>Ids de los productos que se restituyeron por esta via.</returns>
    Task<HashSet<int>> RegisterCancellationAsync(Sale sale, int? userId, string userName, CancellationToken ct = default);
}

public class InventoryService(ApplicationDbContext db, ILogger<InventoryService> logger) : IInventoryService
{
    /// <summary>Mayor cantidad que se acepta en una sola linea (5 enteros y 3 decimales).</summary>
    public const decimal MaxQuantity = 99_999.999m;

    public Task<List<Category>> EligibleCategoriesAsync(CancellationToken ct = default) =>
        db.Categories
            .Include(c => c.ParentCategory)
            .Where(c => c.InventoryEnabled && c.IsActive)
            .OrderBy(c => c.ParentCategory != null ? c.ParentCategory.Name : c.Name)
            .ThenBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<List<InventoryRow>> TrackedRowsAsync(CancellationToken ct = default)
    {
        var products = await db.Products
            .Include(p => p.Category).ThenInclude(c => c!.ParentCategory)
            .Where(p => p.TrackInventory)
            .AsNoTracking()
            .ToListAsync(ct);

        var ids = products.Select(p => p.Id).ToList();

        // Ultima entrada o edicion manual de cada producto: es lo que dice si el
        // numero que se ve esta al dia o es un conteo viejo.
        var lastIds = await db.InventoryMovements
            .Where(m => ids.Contains(m.ProductId) &&
                        (m.Type == InventoryMovementType.Entry || m.Type == InventoryMovementType.Set))
            .GroupBy(m => m.ProductId)
            .Select(g => g.Max(m => m.Id))
            .ToListAsync(ct);

        var last = await db.InventoryMovements
            .Where(m => lastIds.Contains(m.Id))
            .AsNoTracking()
            .ToDictionaryAsync(m => m.ProductId, ct);

        return products
            .Select(p => new InventoryRow
            {
                ProductId = p.Id,
                Code = p.Code,
                Name = p.Name,
                CategoryName = p.Category?.FullName ?? string.Empty,
                Unit = p.Unit,
                SoldByWeight = p.SoldByWeight,
                IsActive = p.IsActive,
                CategoryEligible = p.Category is { InventoryEnabled: true },
                Stock = p.Stock,
                MinStock = p.MinStock,
                LastManualAt = last.TryGetValue(p.Id, out var m) ? m.CreatedAt : null,
                LastManualBy = last.TryGetValue(p.Id, out var m2) ? m2.UserName : null
            })
            .OrderBy(r => r.CategoryName)
            .ThenBy(r => r.Name)
            .ToList();
    }

    public async Task<InventoryResult> TrackAsync(int productId, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return InventoryResult.Fail("El producto no existe.");
        if (!product.IsActive) return InventoryResult.Fail($"{product.Name} está desactivado y no se vende.");
        if (product.Category is not { InventoryEnabled: true })
        {
            return InventoryResult.Fail(
                $"{product.Name} es de la categoría {product.Category?.Name}, que no tiene habilitado el inventario.");
        }

        if (!product.TrackInventory)
        {
            product.TrackInventory = true;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Producto {Code} agregado al inventario", product.Code);
        }
        return new InventoryResult();
    }

    public async Task<InventoryResult> UntrackAsync(int productId, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return InventoryResult.Fail("El producto no existe.");

        // Se conserva la cantidad: si lo vuelven a agregar no hay que empezar de cero.
        if (product.TrackInventory)
        {
            product.TrackInventory = false;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Producto {Code} quitado del inventario", product.Code);
        }
        return new InventoryResult();
    }

    public async Task<InventoryResult> ApplyAsync(InventoryMode mode, IReadOnlyList<InventoryLine> lines,
        int? userId, string userName, string? note, CancellationToken ct = default)
    {
        var result = new InventoryResult();
        if (lines.Count == 0)
        {
            result.Errors.Add("Escriba al menos una cantidad.");
            return result;
        }

        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        foreach (var dup in lines.GroupBy(l => l.ProductId).Where(g => g.Count() > 1))
        {
            var name = products.TryGetValue(dup.Key, out var d) ? d.Name : $"#{dup.Key}";
            result.Errors.Add($"{name} aparece más de una vez.");
        }

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                result.Errors.Add($"El producto #{line.ProductId} no existe.");
                continue;
            }

            var error = ValidateQuantity(mode, product, line.Quantity);
            if (error is not null) result.Errors.Add(error);
        }

        if (!result.Success) return result;

        var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            foreach (var line in lines)
            {
                var (before, after) = await ChangeStockAsync(line.ProductId,
                    current => mode == InventoryMode.Entry ? current + line.Quantity : line.Quantity, ct);

                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = line.ProductId,
                    Type = mode == InventoryMode.Entry ? InventoryMovementType.Entry : InventoryMovementType.Set,
                    Quantity = after - before,
                    StockBefore = before,
                    StockAfter = after,
                    UserId = userId,
                    UserName = userName,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
                });
            }

            await db.SaveChangesAsync(ct);
            if (ownTransaction is not null) await ownTransaction.CommitAsync(ct);
        }
        catch
        {
            if (ownTransaction is not null) await ownTransaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (ownTransaction is not null) await ownTransaction.DisposeAsync();
        }

        result.Rows = await TrackedRowsAsync(ct);
        return result;
    }

    /// <summary>Reglas de la cantidad segun el modo y el tipo de producto.</summary>
    internal static string? ValidateQuantity(InventoryMode mode, Product product, decimal quantity)
    {
        if (!product.TrackInventory) return $"{product.Name} no está en el inventario.";

        if (mode == InventoryMode.Entry && quantity <= 0)
            return $"{product.Name}: la entrada debe ser mayor que cero.";

        if (mode == InventoryMode.Set && quantity < 0)
            return $"{product.Name}: la cantidad no puede ser negativa.";

        if (quantity > MaxQuantity)
            return $"{product.Name}: la cantidad máxima es {MaxQuantity:N3}.";

        if (!product.SoldByWeight && quantity != decimal.Truncate(quantity))
            return $"{product.Name} se cuenta por unidades enteras.";

        if (product.SoldByWeight && Math.Round(quantity, 3) != quantity)
            return $"{product.Name}: el peso admite como máximo 3 decimales.";

        return null;
    }

    public async Task<List<StockShortage>> FindShortagesAsync(IEnumerable<InventoryLine> lines, CancellationToken ct = default)
    {
        var wanted = lines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (wanted.Count == 0) return [];

        var ids = wanted.Keys.ToList();
        var tracked = await db.Products
            .Where(p => ids.Contains(p.Id) && p.TrackInventory)
            .Select(p => new { p.Id, p.Name, p.Unit, p.Stock })
            .AsNoTracking()
            .ToListAsync(ct);

        return tracked
            .Where(p => wanted[p.Id] > p.Stock)
            .Select(p => new StockShortage(p.Id, p.Name, p.Unit, p.Stock, wanted[p.Id]))
            .OrderBy(s => s.ProductName)
            .ToList();
    }

    public async Task RegisterSaleAsync(Sale sale, int? userId, string userName, CancellationToken ct = default)
    {
        var sold = sale.Items
            .Where(i => i.ProductId.HasValue)
            .GroupBy(i => i.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (sold.Count == 0) return;

        var ids = sold.Keys.ToList();
        var tracked = await db.Products
            .Where(p => ids.Contains(p.Id) && p.TrackInventory)
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var productId in tracked)
        {
            var quantity = sold[productId];

            // Si el digital decia 9 y se vendieron 10, el producto estaba en la
            // tienda: se sube a 10 y despues sale todo. Queda en 0, nunca negativo.
            var (before, after) = await ChangeStockAsync(productId,
                current => Math.Max(current, quantity) - quantity, ct);

            if (before < quantity)
            {
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = productId,
                    Type = InventoryMovementType.SaleAdjustment,
                    Quantity = quantity - before,
                    StockBefore = before,
                    StockAfter = quantity,
                    UserId = userId,
                    UserName = userName,
                    SaleId = sale.Id,
                    Note = $"Ajuste automático: el inventario digital no alcanzaba para el recibo {sale.Number}"
                });
            }

            db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = productId,
                Type = InventoryMovementType.Sale,
                Quantity = -quantity,
                StockBefore = Math.Max(before, quantity),
                StockAfter = after,
                UserId = userId,
                UserName = userName,
                SaleId = sale.Id,
                Note = $"Recibo {sale.Number}"
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<int>> RegisterCancellationAsync(Sale sale, int? userId, string userName, CancellationToken ct = default)
    {
        // Se devuelve exactamente lo que esta venta desconto, aunque el producto
        // haya salido del inventario despues: asi la cuenta siempre cuadra.
        var exits = await db.InventoryMovements
            .Where(m => m.SaleId == sale.Id && m.Type == InventoryMovementType.Sale)
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = -g.Sum(m => m.Quantity) })
            .ToListAsync(ct);

        foreach (var exit in exits)
        {
            var (before, after) = await ChangeStockAsync(exit.ProductId, current => current + exit.Quantity, ct);
            db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = exit.ProductId,
                Type = InventoryMovementType.SaleCancelled,
                Quantity = exit.Quantity,
                StockBefore = before,
                StockAfter = after,
                UserId = userId,
                UserName = userName,
                SaleId = sale.Id,
                Note = $"Anulación del recibo {sale.Number}"
            });
        }

        if (exits.Count > 0) await db.SaveChangesAsync(ct);
        return exits.Select(e => e.ProductId).ToHashSet();
    }

    /// <summary>
    /// Cambia el stock de forma atomica: solo escribe si nadie lo modifico desde
    /// que se leyo, y si lo modificaron vuelve a intentar con el valor nuevo.
    /// Asi dos ventas simultaneas del mismo producto no se pisan.
    /// </summary>
    private async Task<(decimal Before, decimal After)> ChangeStockAsync(
        int productId, Func<decimal, decimal> compute, CancellationToken ct)
    {
        // En SQLite el stock se guarda como numero de punto flotante, asi que la
        // comparacion usa una tolerancia menor que el gramo (0.001).
        const decimal tolerance = 0.0005m;

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var before = await db.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync(ct);
            var after = Math.Round(compute(before), 3, MidpointRounding.AwayFromZero);
            var low = before - tolerance;
            var high = before + tolerance;

            var rows = await db.Products
                .Where(p => p.Id == productId && p.Stock >= low && p.Stock <= high)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Stock, after), ct);

            if (rows == 1)
            {
                // Si el producto ya estaba cargado en este contexto, se alinea para
                // que un SaveChanges posterior no escriba el valor viejo encima.
                var entry = db.ChangeTracker.Entries<Product>().FirstOrDefault(e => e.Entity.Id == productId);
                if (entry is not null)
                {
                    entry.Entity.Stock = after;
                    entry.Property(p => p.Stock).OriginalValue = after;
                    entry.Property(p => p.Stock).IsModified = false;
                }
                return (Math.Round(before, 3), after);
            }

            logger.LogDebug("Stock del producto {Id} cambio mientras se actualizaba, reintento {Attempt}", productId, attempt);
        }

        throw new DbUpdateConcurrencyException(
            $"No se pudo actualizar el inventario del producto {productId}: cambió demasiadas veces seguidas.");
    }
}
