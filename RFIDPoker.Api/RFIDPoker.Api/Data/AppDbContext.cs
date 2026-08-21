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
    public DbSet<DeckEntity> Decks => Set<DeckEntity>();
    public DbSet<OverlayToken> OverlayTokens => Set<OverlayToken>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<TournamentDirectorToken> TournamentDirectorTokens => Set<TournamentDirectorToken>();
    public DbSet<RfidDeviceEntity> RfidDevices => Set<RfidDeviceEntity>();
    public DbSet<RfidAntennaEntity> RfidAntennas => Set<RfidAntennaEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CardMappingEntity>(e =>
        {
            e.ToTable("CardMappings");
            e.HasKey(x => new { x.DeckId, x.TagId });
            e.Property(x => x.TagId).HasMaxLength(64);
            e.Property(x => x.Rank).IsRequired();
            e.Property(x => x.Suit).IsRequired();
            e.HasOne(x => x.Deck)
             .WithMany(d => d.Mappings)
             .HasForeignKey(x => x.DeckId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeckEntity>(e =>
        {
            e.ToTable("Decks");
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(d => d.Name).IsUnique();
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

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("AppSettings");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(128);
        });

        modelBuilder.Entity<TournamentDirectorToken>(e =>
        {
            e.ToTable("TournamentDirectorTokens");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<RfidDeviceEntity>(e =>
        {
            e.ToTable("RfidDevices");
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(128).IsRequired();
            e.Property(d => d.WebSocketUrl).HasMaxLength(512).IsRequired();
            e.HasMany(d => d.Antennas)
             .WithOne(a => a.Device!)
             .HasForeignKey(a => a.DeviceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RfidAntennaEntity>(e =>
        {
            e.ToTable("RfidAntennas");
            e.HasKey(a => a.Id);
            e.Property(a => a.Function).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(a => new { a.DeviceId, a.AntennaIndex }).IsUnique();
        });
    }
}

public class CardMappingEntity
{
    public int DeckId { get; set; }
    public DeckEntity? Deck { get; set; }
    public string TagId { get; set; } = string.Empty;
    public Rank Rank { get; set; }
    public Suit Suit { get; set; }
}

public class DeckEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>When true, this deck's mappings are merged into the runtime tag lookup.</summary>
    public bool IsEnabled { get; set; } = true;
    public List<CardMappingEntity> Mappings { get; set; } = [];
}
