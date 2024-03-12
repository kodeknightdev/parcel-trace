using ParcelTrace.Dispatch.Domain;

namespace ParcelTrace.Dispatch.UnitTests;

public class AssignmentSagaTests
{
    private static readonly DateTime Now = new(2024, 3, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private const int MaxAttempts = 3;

    private static AssignmentSaga NewSaga() => AssignmentSaga.Start(Guid.NewGuid(), "PT-2024-ABC234", Now);

    [Fact]
    public void An_offer_waits_for_the_courier_until_its_deadline()
    {
        var saga = NewSaga();
        var lena = Guid.NewGuid();

        var offer = saga.Offer(lena, "Lena", 1.2, Now, Timeout);

        saga.State.Should().Be(SagaState.Offered);
        saga.CurrentOffer.Should().BeSameAs(offer);
        offer.ExpiresAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void Accepting_in_time_completes_the_saga()
    {
        var saga = NewSaga();
        var lena = Guid.NewGuid();
        saga.Offer(lena, "Lena", 1.2, Now, Timeout);

        saga.Accept(lena, Now.AddSeconds(30));

        saga.State.Should().Be(SagaState.Accepted);
        saga.Offers.Single().Outcome.Should().Be(OfferOutcome.Accepted);
        saga.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Only_the_offered_courier_can_answer_and_only_before_the_deadline()
    {
        var saga = NewSaga();
        var lena = Guid.NewGuid();
        saga.Offer(lena, "Lena", 1.2, Now, Timeout);

        saga.Invoking(s => s.Accept(Guid.NewGuid(), Now)).Should().Throw<SagaRuleException>();
        saga.Invoking(s => s.Accept(lena, Now.AddMinutes(3))).Should().Throw<SagaRuleException>().WithMessage("*expired*");
    }

    [Fact]
    public void A_rejection_moves_on_to_the_next_courier()
    {
        var saga = NewSaga();
        var lena = Guid.NewGuid();
        saga.Offer(lena, "Lena", 1.2, Now, Timeout);

        var step = saga.Reject(lena, Now.AddSeconds(10), "Van is full", MaxAttempts);

        step.Should().Be(SagaStep.OfferNextCourier);
        saga.State.Should().Be(SagaState.WaitingForCourier);
        saga.Failures.Should().Be(1);
        saga.TriedCourierIds.Should().Contain(lena, "the same courier is not asked twice");
    }

    [Fact]
    public void Offers_time_out_only_after_their_deadline()
    {
        var saga = NewSaga();
        saga.Offer(Guid.NewGuid(), "Max", 2, Now, Timeout);

        saga.Expire(Now.AddMinutes(1), MaxAttempts).Should().Be(SagaStep.None);
        saga.Expire(Now.AddMinutes(2).AddSeconds(1), MaxAttempts).Should().Be(SagaStep.OfferNextCourier);
        saga.Offers.Single().Outcome.Should().Be(OfferOutcome.TimedOut);
    }

    [Fact]
    public void Three_failures_escalate_to_a_dispatcher()
    {
        var saga = NewSaga();
        var t = Now;

        for (var i = 0; i < 2; i++)
        {
            var courier = Guid.NewGuid();
            saga.Offer(courier, $"Courier {i}", 1, t, Timeout);
            saga.Reject(courier, t, null, MaxAttempts).Should().Be(SagaStep.OfferNextCourier);
        }

        saga.Offer(Guid.NewGuid(), "Last", 1, t, Timeout);
        var step = saga.Expire(t.AddMinutes(5), MaxAttempts);

        step.Should().Be(SagaStep.Escalate);
        saga.State.Should().Be(SagaState.Escalated);
        saga.EscalationReason.Should().Contain("3");
        saga.IsOpen.Should().BeTrue("a dispatcher still has to assign it");
        saga.Invoking(s => s.Offer(Guid.NewGuid(), "x", 1, t, Timeout)).Should().Throw<SagaRuleException>();
    }

    [Fact]
    public void Cancelling_withdraws_the_open_offer()
    {
        var saga = NewSaga();
        var max = Guid.NewGuid();
        saga.Offer(max, "Max", 1, Now, Timeout);

        var withdrawn = saga.Cancel(Now.AddSeconds(5));

        withdrawn!.CourierId.Should().Be(max, "the caller releases the capacity held for this courier");
        withdrawn.Outcome.Should().Be(OfferOutcome.Withdrawn);
        saga.State.Should().Be(SagaState.Cancelled);
    }

    [Fact]
    public void A_dispatcher_can_take_over_an_escalated_saga()
    {
        var saga = NewSaga();
        saga.Offer(Guid.NewGuid(), "A", 1, Now, Timeout);
        saga.Expire(Now.AddHours(1), maxAttempts: 1);

        saga.AssignManually(Now.AddHours(1)).Should().BeNull("there is no open offer after escalation");
        saga.State.Should().Be(SagaState.ManuallyAssigned);
    }
}
