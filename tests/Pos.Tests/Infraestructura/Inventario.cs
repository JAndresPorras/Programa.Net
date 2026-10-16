using Microsoft.EntityFrameworkCore;
using Pos.Web.Models;
using Pos.Web.Services;

namespace Pos.Tests.Infraestructura;

public static class Inventario
{
    /// <summary>Agrega el producto al inventario y deja su cantidad fijada.</summary>
    public static async Task<int> ControlarAsync(this PosAppFactory app, string code, decimal stock)
    {
        var id = (await app.ProductoAsync(code)).Id;
        var track = await app.ServiceAsync<IInventoryService, InventoryResult>(s => s.TrackAsync(id));
        if (!track.Success) throw new InvalidOperationException(string.Join(", ", track.Errors));
        var set = await app.ServiceAsync<IInventoryService, InventoryResult>(
            s => s.ApplyAsync(InventoryMode.Set, [new InventoryLine(id, stock)], 1, "prueba", null));
        if (!set.Success) throw new InvalidOperationException(string.Join(", ", set.Errors));
        return id;
    }

    public static Task<InventoryResult> AplicarAsync(this PosAppFactory app, InventoryMode mode, params (int Id, decimal Qty)[] lines) =>
        app.ServiceAsync<IInventoryService, InventoryResult>(s =>
            s.ApplyAsync(mode, lines.Select(l => new InventoryLine(l.Id, l.Qty)).ToList(), 1, "prueba", "nota de prueba"));

    public static Task<List<InventoryMovement>> MovimientosAsync(this PosAppFactory app, int productId) =>
        app.DbAsync(db => db.InventoryMovements.Where(m => m.ProductId == productId)
            .OrderBy(m => m.Id).AsNoTracking().ToListAsync());

    public static Task<int> VentasAsync(this PosAppFactory app) => app.DbAsync(db => db.Sales.CountAsync());
}
