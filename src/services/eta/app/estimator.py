from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo

EARTH_RADIUS_KM = 6371.0088
ROAD_FACTOR = 1.3

AVERAGE_SPEED_KMH = {"Standard": 24.0, "Express": 30.0}
HANDLING_MINUTES = {"Standard": 12, "Express": 6}
PICKUP_HANDLING_MINUTES = 5

# local hour -> traffic factor
_TRAFFIC_BY_HOUR = {
    **{h: 0.85 for h in (22, 23, 0, 1, 2, 3, 4, 5)},
    6: 1.1,
    7: 1.5,
    8: 1.6,
    9: 1.35,
    **{h: 1.15 for h in range(10, 16)},
    16: 1.45,
    17: 1.65,
    18: 1.5,
    19: 1.2,
    20: 1.0,
    21: 0.95,
}


@dataclass(frozen=True)
class Point:
    lat: float
    lng: float


@dataclass(frozen=True)
class Estimate:
    distance_km: float
    minutes: int
    eta_at: datetime
    traffic_factor: float
    avg_speed_kmh: float


def haversine_km(a: Point, b: Point) -> float:
    d_lat = math.radians(b.lat - a.lat)
    d_lng = math.radians(b.lng - a.lng)
    h = math.sin(d_lat / 2) ** 2 + math.cos(math.radians(a.lat)) * math.cos(math.radians(b.lat)) * math.sin(d_lng / 2) ** 2
    return EARTH_RADIUS_KM * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h))


def traffic_factor(hour: int) -> float:
    if not 0 <= hour <= 23:
        raise ValueError("hour must be 0-23")
    return _TRAFFIC_BY_HOUR[hour]


def traffic_table() -> list[dict[str, float]]:
    return [{"hour": h, "factor": _TRAFFIC_BY_HOUR[h]} for h in range(24)]


def estimate(
    origin: Point,
    destination: Point,
    service_level: str,
    depart_at: datetime | None = None,
    via: Point | None = None,
    tz: str = "Europe/Berlin",
) -> Estimate:
    if service_level not in AVERAGE_SPEED_KMH:
        raise ValueError(f"unknown service level {service_level!r}")

    depart_at = depart_at or datetime.now(timezone.utc)
    if depart_at.tzinfo is None:
        depart_at = depart_at.replace(tzinfo=timezone.utc)

    legs = [origin, via, destination] if via else [origin, destination]
    straight = sum(haversine_km(a, b) for a, b in zip(legs, legs[1:]))
    distance = straight * ROAD_FACTOR

    factor = traffic_factor(depart_at.astimezone(ZoneInfo(tz)).hour)
    speed = AVERAGE_SPEED_KMH[service_level]
    driving = distance / speed * 60 * factor
    handling = HANDLING_MINUTES[service_level] + (PICKUP_HANDLING_MINUTES if via else 0)
    minutes = max(1, math.ceil(driving + handling))

    return Estimate(
        distance_km=round(distance, 2),
        minutes=minutes,
        eta_at=depart_at + timedelta(minutes=minutes),
        traffic_factor=factor,
        avg_speed_kmh=round(speed / factor, 1),
    )
