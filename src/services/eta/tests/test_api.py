from fastapi.testclient import TestClient

from app.config import Settings
from app.main import create_app


def client() -> TestClient:
    return TestClient(create_app(Settings(kafka_enabled=False, otel_enabled=False)))


def test_estimate_returns_camel_case():
    with client() as c:
        res = c.post(
            "/api/eta/estimate",
            json={
                "origin": {"lat": 52.5219, "lng": 13.4132},
                "destination": {"lat": 52.5030, "lng": 13.3320},
                "serviceLevel": "Express",
                "departAt": "2024-05-14T07:00:00Z",
            },
        )
    assert res.status_code == 200
    body = res.json()
    assert set(body) == {"distanceKm", "minutes", "etaAt", "trafficFactor", "avgSpeedKmh"}
    assert body["trafficFactor"] == 1.35  # 09:00 in Berlin


def test_validation_errors_are_422():
    with client() as c:
        res = c.post("/api/eta/estimate", json={"origin": {"lat": 120, "lng": 0}, "destination": {"lat": 0, "lng": 0}})
    assert res.status_code == 422


def test_traffic_and_health():
    with client() as c:
        assert len(c.get("/api/eta/traffic").json()) == 24
        assert c.get("/health").json() == {"status": "Healthy"}
        assert c.get("/health/ready").status_code == 200
