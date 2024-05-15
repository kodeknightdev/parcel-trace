from __future__ import annotations

from opentelemetry import propagate, trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

tracer = trace.get_tracer("parcel-trace-eta")


def configure_tracing(service_name: str) -> None:
    # exporter endpoint from OTEL_EXPORTER_OTLP_ENDPOINT (http://jaeger:4318 in compose)
    provider = TracerProvider(resource=Resource.create({"service.name": service_name, "service.namespace": "parcel-trace"}))
    provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))
    trace.set_tracer_provider(provider)


def extract_from_headers(headers: list[tuple[str, bytes]] | None):
    carrier = {k: v.decode("utf-8") for k, v in (headers or []) if v is not None}
    return propagate.extract(carrier)


def inject_headers() -> list[tuple[str, bytes]]:
    carrier: dict[str, str] = {}
    propagate.inject(carrier)
    return [(k, v.encode("utf-8")) for k, v in carrier.items()]
