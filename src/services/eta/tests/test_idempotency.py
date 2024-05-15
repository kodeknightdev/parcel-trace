from app.idempotency import ProcessedEvents


def test_remembers_processed_events():
    processed = ProcessedEvents()
    assert not processed.seen("a")
    processed.mark("a")
    assert processed.seen("a")


def test_forgets_the_oldest_beyond_capacity():
    processed = ProcessedEvents(capacity=2)
    for event_id in ("a", "b", "c"):
        processed.mark(event_id)
    assert len(processed) == 2
    assert not processed.seen("a")
    assert processed.seen("b") and processed.seen("c")


def test_a_lookup_keeps_an_event_fresh():
    processed = ProcessedEvents(capacity=2)
    processed.mark("a")
    processed.mark("b")
    processed.seen("a")  # touch
    processed.mark("c")
    assert processed.seen("a")
    assert not processed.seen("b")
