import json
import logging
import sys

from opentelemetry import trace


class JsonFormatter(logging.Formatter):
    def __init__(self, service: str):
        super().__init__()
        self.service = service

    def format(self, record: logging.LogRecord) -> str:
        entry = {
            "@t": self.formatTime(record, "%Y-%m-%dT%H:%M:%S%z"),
            "@l": record.levelname,
            "@m": record.getMessage(),
            "logger": record.name,
            "Service": self.service,
        }
        ctx = trace.get_current_span().get_span_context()
        if ctx.is_valid:
            entry["TraceId"] = format(ctx.trace_id, "032x")
            entry["SpanId"] = format(ctx.span_id, "016x")
        for key in ("topic", "event_id", "tracking_number"):
            if hasattr(record, key):
                entry[key] = getattr(record, key)
        if record.exc_info:
            entry["@x"] = self.formatException(record.exc_info)
        return json.dumps(entry)


def configure_logging(service: str, level: str = "INFO") -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter(service))
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(level.upper())
    logging.getLogger("uvicorn.access").setLevel(logging.WARNING)
