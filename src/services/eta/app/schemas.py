from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel

ServiceLevel = Literal["Standard", "Express"]


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class GeoPoint(CamelModel):
    lat: float = Field(ge=-90, le=90)
    lng: float = Field(ge=-180, le=180)


class EstimateRequest(CamelModel):
    origin: GeoPoint
    destination: GeoPoint
    service_level: ServiceLevel = "Standard"
    depart_at: datetime | None = None
    via: GeoPoint | None = Field(default=None, description="Optional stop on the way, e.g. the pick-up address")


class EstimateResponse(CamelModel):
    distance_km: float
    minutes: int
    eta_at: datetime
    traffic_factor: float
    avg_speed_kmh: float


class TrafficHour(CamelModel):
    hour: int
    factor: float


# kafka payloads


class ActiveShipmentRef(CamelModel):
    shipment_id: str
    tracking_number: str
    status: str
    service_level: ServiceLevel
    pickup: GeoPoint
    destination: GeoPoint


class LocationUpdated(CamelModel):
    courier_id: str
    lat: float
    lng: float
    speed_kmh: float | None = None
    recorded_at: datetime
    shipments: list[ActiveShipmentRef] = []


class EtaRecalculated(CamelModel):
    shipment_id: str
    tracking_number: str
    eta_at: datetime
    minutes_remaining: int
    distance_km: float
    traffic_factor: float
    calculated_at: datetime
