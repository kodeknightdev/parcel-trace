using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.Data;

public sealed class ShipmentsDbContext(DbContextOptions<ShipmentsDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(e =>
        {
            e.ToTable("shipments");
            e.HasKey(x => x.Id);
            e.Property(x => x.TrackingNumber).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.TrackingNumber).IsUnique();
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.CourierId);
            e.Property(x => x.CustomerEmail).HasMaxLength(256);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ServiceLevel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.CourierName).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Property(x => x.FailureReason).HasMaxLength(300);
            e.Property(x => x.Version).IsRowVersion();

            e.OwnsOne(x => x.Sender, p => ConfigureParty(p, "sender"));
            e.OwnsOne(x => x.Recipient, p => ConfigureParty(p, "recipient"));
            e.OwnsOne(x => x.Pickup, a => ConfigureAddress(a, "pickup"));
            e.OwnsOne(x => x.Dropoff, a => ConfigureAddress(a, "dropoff"));
            e.OwnsOne(x => x.Package, p =>
            {
                p.Property(x => x.WeightKg).HasColumnName("weight_kg").HasPrecision(6, 2);
                p.Property(x => x.LengthCm).HasColumnName("length_cm");
                p.Property(x => x.WidthCm).HasColumnName("width_cm");
                p.Property(x => x.HeightCm).HasColumnName("height_cm");
            });
            e.OwnsOne(x => x.Proof, p =>
            {
                p.Property(x => x.RecipientName).HasColumnName("pod_recipient_name").HasMaxLength(120);
                p.Property(x => x.SignatureName).HasColumnName("pod_signature_name").HasMaxLength(120);
                p.Property(x => x.PhotoUrl).HasColumnName("pod_photo_url").HasMaxLength(500);
                p.Property(x => x.DeliveredAt).HasColumnName("pod_delivered_at");
            });
        });
    }

    private static void ConfigureParty<T>(OwnedNavigationBuilder<T, Party> p, string prefix) where T : class
    {
        p.Property(x => x.Name).HasColumnName($"{prefix}_name").HasMaxLength(120);
        p.Property(x => x.Phone).HasColumnName($"{prefix}_phone").HasMaxLength(40);
        p.Property(x => x.Email).HasColumnName($"{prefix}_email").HasMaxLength(256);
    }

    private static void ConfigureAddress<T>(OwnedNavigationBuilder<T, Address> a, string prefix) where T : class
    {
        a.Property(x => x.Line1).HasColumnName($"{prefix}_line1").HasMaxLength(200);
        a.Property(x => x.City).HasColumnName($"{prefix}_city").HasMaxLength(100);
        a.Property(x => x.PostalCode).HasColumnName($"{prefix}_postal_code").HasMaxLength(20);
        a.Property(x => x.Country).HasColumnName($"{prefix}_country").HasMaxLength(2);
        a.Property(x => x.Lat).HasColumnName($"{prefix}_lat");
        a.Property(x => x.Lng).HasColumnName($"{prefix}_lng");
        a.Ignore(x => x.Point);
        a.Ignore(x => x.Summary);
    }
}
