using Microsoft.EntityFrameworkCore;
using Pos.Web.Data;
using Pos.Web.Models;

namespace Pos.Tests.Infraestructura;

/// <summary>Consultas de apoyo para que las pruebas no dependan de ids ni de codigos fijos.</summary>
public static class Datos
{
    public static Task<Product> ProductoAsync(this PosAppFactory app, string code) =>
        app.DbAsync(db => db.Products.Include(p => p.Barcodes).Include(p => p.Category)
            .AsNoTracking().FirstAsync(p => p.Code == code));

    public static async Task<string> BarraAsync(this PosAppFactory app, string productCode) =>
        (await app.ProductoAsync(productCode)).Barcodes.OrderByDescending(b => b.IsPrimary).First().Code;

    public static Task<decimal> StockAsync(this PosAppFactory app, string productCode) =>
        app.DbAsync(db => db.Products.Where(p => p.Code == productCode).Select(p => p.Stock).FirstAsync());

    public static Task FijarStockAsync(this PosAppFactory app, string productCode, decimal stock) =>
        app.DbAsync(async db =>
        {
            var p = await db.Products.FirstAsync(x => x.Code == productCode);
            p.Stock = stock;
            await db.SaveChangesAsync();
        });
}
