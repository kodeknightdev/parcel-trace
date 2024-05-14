from __future__ import annotations

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse

from . import estimator
from .config import Settings, get_settings
from .logging_config import configure_logging
from .schemas import EstimateRequest, EstimateResponse, TrafficHour

log = logging.getLogger("eta")


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()
    configure_logging(settings.service_name, settings.log_level)

    if settings.otel_enabled:
        from .telemetry import configure_tracing

        configure_tracing(settings.service_name)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        worker = None
        if settings.kafka_enabled:
            from .kafka_worker import EtaWorker

            worker = EtaWorker(settings)
            worker.start()
        else:
            log.warning("Kafka disabled; only the REST API is running")
        app.state.worker = worker
        yield
        if worker:
            worker.stop()

    app = FastAPI(
        title="parcel-trace eta",
        version="1.0.0",
        description="Estimated time of arrival from distance, service level and an hourly traffic factor.",
        lifespan=lifespan,
    )

    if settings.otel_enabled:
        from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor

        FastAPIInstrumentor.instrument_app(app, excluded_urls="health,health/ready")

    @app.exception_handler(ValueError)
    async def value_error(request: Request, exc: ValueError):
        return JSONResponse(
            status_code=400,
            media_type="application/problem+json",
            content={"type": "about:blank", "title": "Bad request", "status": 400, "detail": str(exc), "instance": request.url.path},
        )

    @app.get("/health", include_in_schema=False)
    def health():
        return {"status": "Healthy"}

    @app.get("/health/ready", include_in_schema=False)
    def ready(request: Request):
        worker = request.app.state.worker
        checks = [{"name": "kafka", "status": "Healthy" if worker is None or worker.running else "Unhealthy"}]
        healthy = all(c["status"] == "Healthy" for c in checks)
        if not healthy:
            raise HTTPException(status_code=503, detail={"status": "Unhealthy", "checks": checks})
        return {"status": "Healthy", "checks": checks}

    @app.post("/api/eta/estimate", response_model=EstimateResponse, response_model_by_alias=True, tags=["ETA"])
    def estimate(request: EstimateRequest) -> EstimateResponse:
        """Travel time from origin (optionally via a stop) to destination."""
        est = estimator.estimate(
            estimator.Point(request.origin.lat, request.origin.lng),
            estimator.Point(request.destination.lat, request.destination.lng),
            request.service_level,
            depart_at=request.depart_at,
            via=estimator.Point(request.via.lat, request.via.lng) if request.via else None,
            tz=settings.timezone,
        )
        return EstimateResponse(
            distance_km=est.distance_km,
            minutes=est.minutes,
            eta_at=est.eta_at,
            traffic_factor=est.traffic_factor,
            avg_speed_kmh=est.avg_speed_kmh,
        )

    @app.get("/api/eta/traffic", response_model=list[TrafficHour], tags=["ETA"])
    def traffic() -> list[dict]:
        """Hourly traffic multipliers (local time)."""
        return estimator.traffic_table()

    return app


app = create_app()
