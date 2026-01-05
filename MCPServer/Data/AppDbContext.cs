using MCPServer.Models;
using Microsoft.EntityFrameworkCore;

namespace MCPServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CheckinDenseDto> Checkins { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CheckinDenseDto>()
            .HasKey(c => c.CheckinId);

        modelBuilder.Entity<CheckinDenseDto>()
            .HasIndex(c => new { c.SiteId, c.VisitPropertyId });

        var sampleData = CheckinDenseSampleData.Generate(200);
        modelBuilder.Entity<CheckinDenseDto>()
            .HasData(sampleData);
    }
}