using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.UnitTests;

public class ShipmentTests
{
    private static readonly DateTimeOffset Created = new(2024, 2, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid CourierId = Guid.NewGuid();

    private static Shipment NewShipment() => new()
    {
        TrackingNumber = "PT-2024-ABC234",
        CreatedAt = Created,
        CancelDeadline = Created.AddMinutes(30),
        SlaDueAt = Created.AddHours(8),
        ServiceLevel = ServiceLevel.Express
    };

    private static DeliveryProof Proof(DateTimeOffset at) => new() { RecipientName = "Jonas Weber", SignatureName = "J. Weber", DeliveredAt = at };

    [Fact]
    public void Can_cancel_inside_the_window()
    {
        var shipment = NewShipment();

        shipment.Cancel(Created.AddMinutes(10), "Wrong address");

        shipment.Status.Should().Be(ShipmentStatus.Cancelled);
        shipment.CancelReason.Should().Be("Wrong address");
    }

    [Fact]
    public void Cannot_cancel_after_the_window()
    {
        var shipment = NewShipment();

        var act = () => shipment.Cancel(Created.AddMinutes(31), "Too late");

        act.Should().Throw<ShipmentRuleException>().WithMessage("*window*");
        shipment.Status.Should().Be(ShipmentStatus.Created);
    }

    [Fact]
    public void Cannot_cancel_once_picked_up_even_inside_the_window()
    {
        var shipment = NewShipment();
        shipment.Assign(CourierId, "Lena Fischer", Created.AddMinutes(2));
        shipment.ApplyStatus(ShipmentStatus.PickedUp, Created.AddMinutes(5));

        shipment.CanCancel(Created.AddMinutes(6)).Should().BeFalse();
        shipment.Invoking(s => s.Cancel(Created.AddMinutes(6), "x")).Should().Throw<ShipmentRuleException>();
    }

    [Fact]
    public void Full_lifecycle_records_timestamps_and_proof()
    {
        var shipment = NewShipment();

        shipment.Assign(CourierId, "Lena Fischer", Created.AddMinutes(1)).Should().BeTrue();
        shipment.ApplyStatus(ShipmentStatus.PickedUp, Created.AddMinutes(20)).Should().BeTrue();
        shipment.ApplyStatus(ShipmentStatus.InTransit, Created.AddMinutes(25)).Should().BeTrue();
        shipment.ApplyStatus(ShipmentStatus.OutForDelivery, Created.AddMinutes(40)).Should().BeTrue();
        shipment.ApplyStatus(ShipmentStatus.Delivered, Created.AddMinutes(55), proof: Proof(Created.AddMinutes(55))).Should().BeTrue();

        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        shipment.CourierName.Should().Be("Lena Fischer");
        shipment.PickedUpAt.Should().Be(Created.AddMinutes(20));
        shipment.DeliveredAt.Should().Be(Created.AddMinutes(55));
        shipment.Proof!.SignatureName.Should().Be("J. Weber");
        shipment.IsDelayed(Created.AddDays(2)).Should().BeFalse("delivered shipments are never delayed");
    }

    [Fact]
    public void Redelivered_and_stale_events_are_ignored()
    {
        var shipment = NewShipment();
        shipment.Assign(CourierId, "Lena Fischer", Created.AddMinutes(1));
        shipment.ApplyStatus(ShipmentStatus.PickedUp, Created.AddMinutes(20));

        shipment.ApplyStatus(ShipmentStatus.PickedUp, Created.AddMinutes(20)).Should().BeFalse();
        shipment.Assign(CourierId, "Lena Fischer", Created.AddMinutes(1)).Should().BeFalse();
        shipment.Status.Should().Be(ShipmentStatus.PickedUp);
    }

    [Fact]
    public void Status_before_assignment_throws_so_the_event_is_retried()
    {
        var shipment = NewShipment();

        shipment.Invoking(s => s.ApplyStatus(ShipmentStatus.PickedUp, Created)).Should().Throw<ShipmentRuleException>();
    }

    [Fact]
    public void Reassignment_before_pickup_changes_the_courier()
    {
        var shipment = NewShipment();
        shipment.Assign(CourierId, "Lena Fischer", Created.AddMinutes(1));

        var other = Guid.NewGuid();
        shipment.Assign(other, "Max Keller", Created.AddMinutes(3)).Should().BeTrue();

        shipment.CourierId.Should().Be(other);
    }

    [Fact]
    public void Delivery_requires_proof()
    {
        var shipment = NewShipment();
        shipment.Assign(CourierId, "Lena", Created);
        shipment.ApplyStatus(ShipmentStatus.PickedUp, Created);
        shipment.ApplyStatus(ShipmentStatus.OutForDelivery, Created);

        shipment.Invoking(s => s.ApplyStatus(ShipmentStatus.Delivered, Created)).Should().Throw<ShipmentRuleException>().WithMessage("*proof*");
    }

    [Fact]
    public void Delay_detection_uses_sla_and_eta()
    {
        var shipment = NewShipment();
        shipment.IsDelayed(Created.AddHours(1)).Should().BeFalse();

        shipment.UpdateEta(Created.AddHours(9), Created.AddHours(1)).Should().BeTrue();
        shipment.IsDelayed(Created.AddHours(1)).Should().BeTrue("the ETA is past the 8h promise");

        shipment.UpdateEta(Created.AddHours(7), Created.AddMinutes(30)).Should().BeFalse("an older calculation never replaces a newer one");
        shipment.IsDelayed(Created.AddHours(9)).Should().BeTrue();
    }
}
