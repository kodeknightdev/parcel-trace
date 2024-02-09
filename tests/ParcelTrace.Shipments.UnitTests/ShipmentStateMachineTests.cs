using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.UnitTests;

public class ShipmentStateMachineTests
{
    [Theory]
    [InlineData(ShipmentStatus.Created, ShipmentStatus.Assigned)]
    [InlineData(ShipmentStatus.Assigned, ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.PickedUp, ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.PickedUp, ShipmentStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.Failed)]
    [InlineData(ShipmentStatus.Created, ShipmentStatus.Cancelled)]
    [InlineData(ShipmentStatus.Assigned, ShipmentStatus.Cancelled)]
    public void Allows_the_happy_path_and_cancellation_before_pickup(ShipmentStatus from, ShipmentStatus to) =>
        ShipmentStateMachine.Evaluate(from, to).Should().Be(TransitionOutcome.Apply);

    [Theory]
    [InlineData(ShipmentStatus.Created, ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.Assigned, ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.InTransit, ShipmentStatus.Delivered)]
    public void Skipping_ahead_is_invalid(ShipmentStatus from, ShipmentStatus to) =>
        ShipmentStateMachine.Evaluate(from, to).Should().Be(TransitionOutcome.Invalid);

    [Theory]
    [InlineData(ShipmentStatus.PickedUp, ShipmentStatus.Assigned)]
    [InlineData(ShipmentStatus.OutForDelivery, ShipmentStatus.InTransit)]
    public void Late_events_are_stale(ShipmentStatus from, ShipmentStatus to) =>
        ShipmentStateMachine.Evaluate(from, to).Should().Be(TransitionOutcome.Stale);

    [Theory]
    [InlineData(ShipmentStatus.Delivered, ShipmentStatus.Failed)]
    [InlineData(ShipmentStatus.Cancelled, ShipmentStatus.Assigned)]
    [InlineData(ShipmentStatus.Failed, ShipmentStatus.OutForDelivery)]
    public void Nothing_happens_after_the_end(ShipmentStatus from, ShipmentStatus to) =>
        ShipmentStateMachine.Evaluate(from, to).Should().Be(TransitionOutcome.Terminal);

    [Fact]
    public void Same_status_is_a_duplicate() =>
        ShipmentStateMachine.Evaluate(ShipmentStatus.InTransit, ShipmentStatus.InTransit).Should().Be(TransitionOutcome.Duplicate);

    [Fact]
    public void Picked_up_parcels_can_no_longer_be_cancelled() =>
        ShipmentStateMachine.CanTransition(ShipmentStatus.PickedUp, ShipmentStatus.Cancelled).Should().BeFalse();

    [Fact]
    public void Every_status_has_an_entry() =>
        Enum.GetValues<ShipmentStatus>().Invoking(all => all.Select(ShipmentStateMachine.NextStatuses).ToList()).Should().NotThrow();
}
