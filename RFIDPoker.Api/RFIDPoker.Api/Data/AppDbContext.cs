using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CardMappingEntity> CardMappings => Set<CardMappingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardMappingEntity>(e =>
        {
            e.ToTable("CardMappings");
            e.HasKey(x => x.TagId);
            e.Property(x => x.TagId).HasMaxLength(64);
            e.Property(x => x.Rank).IsRequired();
            e.Property(x => x.Suit).IsRequired();
        });
    }
}

public class CardMappingEntity
{
    public string TagId { get; set; } = string.Empty;
    public Rank Rank { get; set; }
    public Suit Suit { get; set; }
}
