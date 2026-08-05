using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WhosHome.Server.Data;

public class WhosHomeContext(DbContextOptions<WhosHomeContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    public DbSet<PositionReport> Reports => Set<PositionReport>();

    public DbSet<DeviceSubscription> Subscriptions => Set<DeviceSubscription>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

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

        modelBuilder.Entity<DeviceSubscription>(entity =>
        {
            // One row per browser install. Re-subscribing the same browser should replace rather
            // than duplicate, so the endpoint is the natural key.
            entity.HasIndex(subscription => subscription.Endpoint).IsUnique();
            entity.Property(subscription => subscription.Endpoint).HasMaxLength(500);
            entity.HasOne(subscription => subscription.Person)
                .WithMany(person => person.Subscriptions)
                .HasForeignKey(subscription => subscription.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasIndex(preference => new { preference.SubscriberPersonId, preference.SubjectPersonId })
                .IsUnique();

            // Removing a person clears both the preferences they set and the ones others set
            // about them. NoAction on one side because SQLite will not accept two cascade paths
            // into the same table.
            entity.HasOne(preference => preference.Subscriber)
                .WithMany()
                .HasForeignKey(preference => preference.SubscriberPersonId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(preference => preference.Subject)
                .WithMany()
                .HasForeignKey(preference => preference.SubjectPersonId)
                .OnDelete(DeleteBehavior.ClientCascade);
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
