using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TeamPilot.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct the DbContext at design time by reading
/// appsettings directly, without needing the API host project to build first.
/// </summary>
public class TeamPilotDbContextFactory : IDesignTimeDbContextFactory<TeamPilotDbContext>
{
    public TeamPilotDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=TeamPilotDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true";

        var optionsBuilder = new DbContextOptionsBuilder<TeamPilotDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new TeamPilotDbContext(optionsBuilder.Options);
    }
}
