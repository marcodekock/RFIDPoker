using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RFIDPoker.Api.Auth;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    public DbSet<CardMappingEntity> CardMappings => Set<CardMappingEntity>();
    public DbSet<OverlayToken> OverlayTokens => Set<OverlayToken>();
    public DbSet<Camera> Cameras => Set<Camera>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CardMappingEntity>(e =>
        {
            e.ToTable("CardMappings");
            e.HasKey(x => x.TagId);
            e.Property(x => x.TagId).HasMaxLength(64);
            e.Property(x => x.Rank).IsRequired();
            e.Property(x => x.Suit).IsRequired();
        });

        modelBuilder.Entity<OverlayToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Camera>(e =>
        {
            e.ToTable("Cameras");
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(128).IsRequired();
            e.Property(c => c.ObsSceneName).HasMaxLength(128).IsRequired();
            e.Property(c => c.Role).HasConversion<string>().HasMaxLength(16);
        });
    }
}

public class CardMappingEntity
{
    public string TagId { get; set; } = string.Empty;
    public Rank Rank { get; set; }
    public Suit Suit { get; set; }
}
