using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Web.Data;

/// <summary>Permite a "dotnet ef migrations" crear el contexto SQLite sin arrancar la app.</summary>
public class SqliteDbContextFactory : IDesignTimeDbContextFactory<SqliteApplicationDbContext>
{
    public SqliteApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqliteApplicationDbContext>()
            .UseSqlite("Data Source=pos.db")
            .Options;
        return new SqliteApplicationDbContext(options);
    }
}

/// <summary>Permite a "dotnet ef migrations" crear el contexto SQL Server sin arrancar la app.</summary>
public class SqlServerDbContextFactory : IDesignTimeDbContextFactory<SqlServerApplicationDbContext>
{
    public SqlServerApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqlServerApplicationDbContext>()
            .UseSqlServer("Server=.;Database=PosBarras;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new SqlServerApplicationDbContext(options);
    }
}
