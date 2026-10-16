using Pos.Tests.Infraestructura;
using Pos.Web.Models;
using Pos.Web.Services;

namespace Pos.Tests;

/// <summary>Reglas de las entradas, ediciones y la seleccion de productos.</summary>
public class InventarioReglasTests
{
    [Fact]
    public async Task Entrada_suma_a_lo_que_hay()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 5);

        var r = await app.AplicarAsync(InventoryMode.Entry, (id, 10));

        Assert.True(r.Success);
        Assert.Equal(15m, await app.StockAsync("QUE-002"));
        var ultimo = (await app.MovimientosAsync(id)).Last();
        Assert.Equal(InventoryMovementType.Entry, ultimo.Type);
        Assert.Equal((10m, 5m, 15m), (ultimo.Quantity, ultimo.StockBefore, ultimo.StockAfter));
        Assert.Equal("prueba", ultimo.UserName);
        Assert.Equal("nota de prueba", ultimo.Note);
    }

    [Fact]
    public async Task Editar_cantidad_reemplaza_y_no_suma()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 5);

        var r = await app.AplicarAsync(InventoryMode.Set, (id, 10));

        Assert.True(r.Success);
        Assert.Equal(10m, await app.StockAsync("QUE-002"));
        var ultimo = (await app.MovimientosAsync(id)).Last();
        Assert.Equal(InventoryMovementType.Set, ultimo.Type);
        Assert.Equal((5m, 5m, 10m), (ultimo.Quantity, ultimo.StockBefore, ultimo.StockAfter));
    }

    [Fact]
    public async Task Editar_a_cero_se_permite_y_a_negativo_no()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 5);

        Assert.True((await app.AplicarAsync(InventoryMode.Set, (id, 0))).Success);
        Assert.Equal(0m, await app.StockAsync("QUE-002"));

        var negativo = await app.AplicarAsync(InventoryMode.Set, (id, -1));
        Assert.False(negativo.Success);
        Assert.Contains("negativa", negativo.Errors[0]);
        Assert.Equal(0m, await app.StockAsync("QUE-002"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Entrada_debe_ser_mayor_que_cero(int cantidad)
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 5);

        var r = await app.AplicarAsync(InventoryMode.Entry, (id, cantidad));

        Assert.False(r.Success);
        Assert.Equal(5m, await app.StockAsync("QUE-002"));
    }

    [Fact]
    public async Task Productos_por_unidad_no_aceptan_decimales()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 5);

        var r = await app.AplicarAsync(InventoryMode.Entry, (id, 2.5m));

        Assert.False(r.Success);
        Assert.Contains("unidades enteras", r.Errors[0]);
    }

    [Fact]
    public async Task Productos_por_peso_aceptan_tres_decimales_pero_no_cuatro()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-001", 1.5m);   // queso fresco por kilo

        Assert.True((await app.AplicarAsync(InventoryMode.Entry, (id, 1.755m))).Success);
        Assert.Equal(3.255m, await app.StockAsync("QUE-001"));

        var cuatro = await app.AplicarAsync(InventoryMode.Entry, (id, 0.0005m));
        Assert.False(cuatro.Success);
        Assert.Contains("3 decimales", cuatro.Errors[0]);
    }

    [Fact]
    public async Task Cantidad_maxima_por_linea()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 0);
        Assert.True((await app.AplicarAsync(InventoryMode.Set, (id, 99999))).Success);
        Assert.False((await app.AplicarAsync(InventoryMode.Set, (id, 100000))).Success);
    }

    [Fact]
    public async Task No_se_carga_cantidad_a_un_producto_que_no_esta_en_el_inventario()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = (await app.ProductoAsync("YOG-001")).Id;

        var r = await app.AplicarAsync(InventoryMode.Set, (id, 10));

        Assert.False(r.Success);
        Assert.Contains("no está en el inventario", r.Errors[0]);
    }

    [Fact]
    public async Task Un_lote_con_una_linea_mala_no_guarda_ninguna()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var queso = await app.ControlarAsync("QUE-002", 5);
        var yogurt = await app.ControlarAsync("YOG-001", 7);

        var r = await app.AplicarAsync(InventoryMode.Entry, (queso, 10), (yogurt, -2));

        Assert.False(r.Success);
        Assert.Equal(5m, await app.StockAsync("QUE-002"));   // la buena tampoco se aplico
        Assert.Equal(7m, await app.StockAsync("YOG-001"));
    }

    [Fact]
    public async Task Un_producto_repetido_en_el_lote_se_rechaza()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var queso = await app.ControlarAsync("QUE-002", 5);

        var r = await app.AplicarAsync(InventoryMode.Entry, (queso, 1), (queso, 2));

        Assert.False(r.Success);
        Assert.Contains("más de una vez", r.Errors[0]);
        Assert.Equal(5m, await app.StockAsync("QUE-002"));
    }

    [Fact]
    public async Task Solo_se_eligen_productos_de_categorias_habilitadas_y_activos()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var agua = (await app.ProductoAsync("BEB-001")).Id;   // Bebidas: sin inventario
        var r = await app.ServiceAsync<IInventoryService, InventoryResult>(s => s.TrackAsync(agua));
        Assert.False(r.Success);
        Assert.Contains("no tiene habilitado el inventario", r.Errors[0]);

        var yogurt = (await app.ProductoAsync("YOG-002")).Id;
        await app.DbAsync(async db => { (await db.Products.FindAsync(yogurt))!.IsActive = false; await db.SaveChangesAsync(); });
        var inactivo = await app.ServiceAsync<IInventoryService, InventoryResult>(s => s.TrackAsync(yogurt));
        Assert.False(inactivo.Success);
        Assert.Contains("desactivado", inactivo.Errors[0]);
    }

    [Fact]
    public async Task Ningun_producto_se_controla_hasta_que_el_usuario_lo_elige()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var controlados = await app.DbAsync(db => Task.FromResult(db.Products.Count(p => p.TrackInventory)));
        Assert.Equal(0, controlados);
    }

    [Fact]
    public async Task Agregar_dos_veces_es_inofensivo_y_quitar_conserva_la_cantidad()
    {
        await using var app = new PosAppFactory(); _ = app.CreateClient();
        var id = await app.ControlarAsync("QUE-002", 8);
        Assert.True((await app.ServiceAsync<IInventoryService, InventoryResult>(s => s.TrackAsync(id))).Success);

        await app.ServiceAsync<IInventoryService, InventoryResult>(s => s.UntrackAsync(id));
        Assert.False((await app.ProductoAsync("QUE-002")).TrackInventory);
        Assert.Equal(8m, await app.StockAsync("QUE-002"));

        // Volver a agregarlo no obliga a empezar de cero.
        await app.ServiceAsync<IInventoryService, InventoryResult>(s => s.TrackAsync(id));
        var fila = (await app.ServiceAsync<IInventoryService, List<InventoryRow>>(s => s.TrackedRowsAsync())).Single();
        Assert.Equal(8m, fila.Stock);
        Assert.NotNull(fila.LastManualAt);
        Assert.Equal("Lácteos › Quesos", fila.CategoryName);
    }
}
