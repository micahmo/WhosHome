using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WhosHome.Server.Data;

public class WhosHomeContext(DbContextOptions<WhosHomeContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    public DbSet<PositionReport> Reports => Set<PositionReport>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset, and the default text mapping cannot be used in
        // ORDER BY. The binary converter stores a sortable long, which matters because every
        // presence lookup orders by the most recent report.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasIndex(person => person.DeviceId).IsUnique();
            entity.HasIndex(person => person.SetupToken);
            entity.Property(person => person.Name).HasMaxLength(100);
            entity.Property(person => person.DeviceId).HasMaxLength(64);
            entity.Property(person => person.SetupToken).HasMaxLength(64);
        });

        modelBuilder.Entity<PositionReport>(entity =>
        {
            entity.HasIndex(report => new { report.PersonId, report.ReceivedUtc });
            entity.HasOne(report => report.Person)
                .WithMany(person => person.Reports)
                .HasForeignKey(report => report.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
