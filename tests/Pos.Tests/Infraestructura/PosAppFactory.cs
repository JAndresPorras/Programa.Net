using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pos.Web.Data;

namespace Pos.Tests.Infraestructura;

/// <summary>
/// Levanta la aplicacion real (migraciones, siembra, autorizacion, vistas) contra
/// una base SQLite temporal propia. Cada prueba usa la suya, asi no se pisan.
/// Pasando la misma ruta a una segunda fabrica se simula un reinicio del servidor.
/// </summary>
public sealed class PosAppFactory : WebApplicationFactory<Program>
{
    public string DbPath { get; }
    private readonly bool _deleteOnDispose;

    public PosAppFactory(string? dbPath = null)
    {
        _deleteOnDispose = dbPath is null;
        DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"pos-prueba-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        // Sin pool: al terminar la prueba el archivo se puede borrar y un
        // "reinicio" con otra fabrica ve exactamente lo que quedo en disco.
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath};Pooling=False");
        builder.ConfigureLogging(l => l.ClearProviders());
    }

    /// <summary>Ejecuta codigo con un DbContext de la aplicacion.</summary>
    public async Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }

    public Task DbAsync(Func<ApplicationDbContext, Task> action) =>
        DbAsync<bool>(async db => { await action(db); return true; });

    public async Task<T> ServiceAsync<TService, T>(Func<TService, Task<T>> action) where TService : notnull
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_deleteOnDispose)
        {
            foreach (var file in new[] { DbPath, DbPath + "-wal", DbPath + "-shm" })
            {
                try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
            }
        }
    }
}
