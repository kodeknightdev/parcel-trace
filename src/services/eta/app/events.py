from __future__ import annotations

import uuid
from datetime import datetime, timezone

from . import estimator
from .schemas import EtaRecalculated, LocationUpdated

TOPIC_LOCATION_UPDATED = "tracking.location-updated"
TOPIC_ETA_RECALCULATED = "eta.recalculated"
EVENT_ETA_RECALCULATED = "EtaRecalculated"

# still has to go to the pick-up first
NEEDS_PICKUP = {"Assigned"}
ON_THE_WAY = {"PickedUp", "InTransit", "OutForDelivery"}


def recalculate(update: LocationUpdated, tz: str = "Europe/Berlin", now: datetime | None = None) -> list[EtaRecalculated]:
    now = now or datetime.now(timezone.utc)
    courier = estimator.Point(update.lat, update.lng)
    results: list[EtaRecalculated] = []

    for shipment in update.shipments:
        if shipment.status not in NEEDS_PICKUP | ON_THE_WAY:
            continue
        via = estimator.Point(shipment.pickup.lat, shipment.pickup.lng) if shipment.status in NEEDS_PICKUP else None
        est = estimator.estimate(
            courier,
            estimator.Point(shipment.destination.lat, shipment.destination.lng),
            shipment.service_level,
            depart_at=now,
            via=via,
            tz=tz,
        )
        results.append(
            EtaRecalculated(
                shipment_id=shipment.shipment_id,
                tracking_number=shipment.tracking_number,
                eta_at=est.eta_at,
                minutes_remaining=est.minutes,
                distance_km=est.distance_km,
                traffic_factor=est.traffic_factor,
                calculated_at=now,
            )
        )
    return results


def envelope(event_type: str, payload: EtaRecalculated, now: datetime | None = None) -> dict:
    return {
        "eventId": str(uuid.uuid4()),
        "eventType": event_type,
        "schemaVersion": 1,
        "occurredAt": (now or datetime.now(timezone.utc)).isoformat(),
        "payload": payload.model_dump(mode="json", by_alias=True),
    }
