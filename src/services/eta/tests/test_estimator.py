from datetime import datetime, timezone

import pytest

from app import estimator
from app.estimator import Point

ALEXANDERPLATZ = Point(52.5219, 13.4132)
KUDAMM = Point(52.5030, 13.3320)


def test_haversine_matches_known_distance():
    # Alexanderplatz -> Kurfürstendamm is about 5.9 km as the crow flies
    assert estimator.haversine_km(ALEXANDERPLATZ, KUDAMM) == pytest.approx(5.9, abs=0.1)


def test_rush_hour_is_slower_than_night():
    rush = estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Standard", datetime(2024, 5, 14, 15, 30, tzinfo=timezone.utc))  # 17:30 Berlin
    night = estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Standard", datetime(2024, 5, 14, 0, 30, tzinfo=timezone.utc))  # 02:30 Berlin
    assert rush.traffic_factor == 1.65
    assert night.traffic_factor == 0.85
    assert rush.minutes > night.minutes


def test_express_beats_standard():
    at = datetime(2024, 5, 14, 9, 0, tzinfo=timezone.utc)
    assert estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Express", at).minutes < estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Standard", at).minutes


def test_pickup_stop_adds_distance_and_handling():
    at = datetime(2024, 5, 14, 9, 0, tzinfo=timezone.utc)
    direct = estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Standard", at)
    via = estimator.estimate(Point(52.54, 13.42), KUDAMM, "Standard", at, via=ALEXANDERPLATZ)
    assert via.distance_km > direct.distance_km
    assert via.minutes >= direct.minutes + estimator.PICKUP_HANDLING_MINUTES


def test_eta_is_departure_plus_minutes_and_never_zero():
    at = datetime(2024, 5, 14, 9, 0, tzinfo=timezone.utc)
    est = estimator.estimate(KUDAMM, KUDAMM, "Express", at)
    assert est.distance_km == 0
    assert est.minutes == estimator.HANDLING_MINUTES["Express"]
    assert (est.eta_at - at).total_seconds() == est.minutes * 60


def test_naive_departure_is_treated_as_utc():
    est = estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Standard", datetime(2024, 5, 14, 9, 0))
    assert est.eta_at.tzinfo is not None


def test_rejects_unknown_service_level_and_hours():
    with pytest.raises(ValueError):
        estimator.estimate(ALEXANDERPLATZ, KUDAMM, "Overnight")
    with pytest.raises(ValueError):
        estimator.traffic_factor(24)


def test_traffic_table_covers_every_hour():
    table = estimator.traffic_table()
    assert [row["hour"] for row in table] == list(range(24))
