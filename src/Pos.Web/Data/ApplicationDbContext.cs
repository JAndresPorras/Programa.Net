using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pos.Web.Models;

namespace Pos.Web.Data;

/// <summary>
/// Contexto base con todo el modelo. Se deriva en un contexto por proveedor
/// (SQLite para instalacion local, SQL Server para base de datos en la nube)
/// porque las migraciones de EF Core son especificas de cada motor.
/// </summary>
public abstract class ApplicationDbContext(DbContextOptions options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, int>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Barcode> Barcodes => Set<Barcode>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            e.Property(u => u.DocumentId).HasMaxLength(30);
        });

        builder.Entity<ApplicationRole>(e => e.Property(r => r.Description).HasMaxLength(250));

        builder.Entity<Category>(e =>
        {
            e.HasIndex(c => c.Name).IsUnique();
            // Una categoria principal con subcategorias no se puede borrar sin antes moverlas.
            e.HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Code).IsUnique();
            e.HasIndex(p => p.Name);
            e.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Barcode>(e =>
        {
            // El codigo de barras es la llave de busqueda de la pistola: debe ser unico.
            e.HasIndex(b => b.Code).IsUnique();
            e.HasOne(b => b.Product)
                .WithMany(p => p.Barcodes)
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PriceHistory>(e =>
        {
            e.HasIndex(h => new { h.ProductId, h.ChangedAt });
            e.HasOne(h => h.Product)
                .WithMany(p => p.PriceHistories)
                .HasForeignKey(h => h.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.ChangedByUser)
                .WithMany()
                .HasForeignKey(h => h.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Sale>(e =>
        {
            e.HasIndex(s => s.Number).IsUnique();
            e.HasIndex(s => s.SoldAt);
            e.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.CashSession)
                .WithMany(c => c.Sales)
                .HasForeignKey(s => s.CashSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SaleItem>(e =>
        {
            e.HasIndex(i => i.SaleId);
            e.HasOne(i => i.Sale)
                .WithMany(s => s.Items)
                .HasForeignKey(i => i.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CashSession>(e =>
        {
            e.HasIndex(c => new { c.UserId, c.Status });
            e.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CashMovement>(e =>
        {
            e.HasOne(m => m.CashSession)
                .WithMany(c => c.Movements)
                .HasForeignKey(m => m.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InventoryMovement>(e =>
        {
            e.HasIndex(m => new { m.ProductId, m.CreatedAt });
            e.HasIndex(m => m.SaleId);
            e.HasOne(m => m.Product)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AppSetting>(e => e.HasIndex(s => s.Key).IsUnique());

        builder.Entity<AuditLog>(e => e.HasIndex(a => a.CreatedAt));

        // SQLite no sabe ordenar ni sumar decimal(18,4) de forma nativa: se guarda
        // como TEXT por defecto y rompe las comparaciones. Lo mapeamos a double
        // solo para ese proveedor conservando decimal en el modelo de C#.
        if (Database.IsSqlite())
        {
            foreach (var property in builder.Model.GetEntityTypes()
                         .SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetColumnType("decimal(18,4)");
                property.SetProviderClrType(typeof(double));
            }
        }
    }
}

/// <summary>Contexto para instalacion local con SQLite (sin servidor, un solo archivo).</summary>
public class SqliteApplicationDbContext(DbContextOptions<SqliteApplicationDbContext> options)
    : ApplicationDbContext(options);

/// <summary>Contexto para SQL Server local o en la nube (Azure SQL), varios puntos de venta.</summary>
public class SqlServerApplicationDbContext(DbContextOptions<SqlServerApplicationDbContext> options)
    : ApplicationDbContext(options);
