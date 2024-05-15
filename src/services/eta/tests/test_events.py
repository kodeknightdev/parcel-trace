from datetime import datetime, timezone

from app.events import envelope, recalculate
from app.schemas import LocationUpdated

NOW = datetime(2024, 5, 14, 9, 0, tzinfo=timezone.utc)


def update(*statuses: str) -> LocationUpdated:
    return LocationUpdated.model_validate(
        {
            "courierId": "c0de0000-0000-4000-8000-000000000002",
            "lat": 52.52,
            "lng": 13.40,
            "recordedAt": "2024-05-14T09:00:00Z",
            "shipments": [
                {
                    "shipmentId": f"00000000-0000-0000-0000-00000000000{i}",
                    "trackingNumber": f"PT-2024-AAAAA{i}",
                    "status": status,
                    "serviceLevel": "Standard",
                    "pickup": {"lat": 52.5219, "lng": 13.4132},
                    "destination": {"lat": 52.5030, "lng": 13.3320},
                }
                for i, status in enumerate(statuses)
            ],
        }
    )


def test_one_recalculation_per_open_shipment():
    results = recalculate(update("Assigned", "InTransit", "Delivered"), now=NOW)
    assert [r.tracking_number for r in results] == ["PT-2024-AAAAA0", "PT-2024-AAAAA1"]
    assert all(r.calculated_at == NOW for r in results)


def test_assigned_shipments_route_via_pickup():
    assigned, in_transit = recalculate(update("Assigned", "InTransit"), now=NOW)
    assert assigned.distance_km > in_transit.distance_km
    assert assigned.minutes_remaining > in_transit.minutes_remaining


def test_no_shipments_means_no_events():
    assert recalculate(update(), now=NOW) == []


def test_envelope_matches_the_shared_wire_format():
    (result,) = recalculate(update("OutForDelivery"), now=NOW)
    env = envelope("EtaRecalculated", result, now=NOW)
    assert env["eventType"] == "EtaRecalculated"
    assert env["schemaVersion"] == 1
    assert env["payload"]["trackingNumber"] == "PT-2024-AAAAA0"
    assert "minutesRemaining" in env["payload"] and "etaAt" in env["payload"]
