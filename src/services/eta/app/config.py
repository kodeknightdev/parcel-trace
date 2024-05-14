import os
from dataclasses import dataclass, field


def _bool(name: str, default: bool) -> bool:
    value = os.getenv(name)
    return default if value in (None, "") else value.lower() in ("1", "true", "yes")


@dataclass(frozen=True)
class Settings:
    service_name: str = field(default_factory=lambda: os.getenv("OTEL_SERVICE_NAME", "eta"))
    log_level: str = field(default_factory=lambda: os.getenv("LOG_LEVEL", "INFO"))
    kafka_enabled: bool = field(default_factory=lambda: _bool("KAFKA_ENABLED", True))
    kafka_brokers: str = field(default_factory=lambda: os.getenv("KAFKA_BROKERS", "localhost:9094"))
    kafka_group_id: str = field(default_factory=lambda: os.getenv("KAFKA_GROUP_ID", "eta"))
    kafka_max_retries: int = field(default_factory=lambda: int(os.getenv("KAFKA_MAX_RETRIES", "3")))
    otel_enabled: bool = field(default_factory=lambda: os.getenv("OTEL_SDK_DISABLED", "false").lower() != "true")
    timezone: str = field(default_factory=lambda: os.getenv("TRAFFIC_TIMEZONE", "Europe/Berlin"))


def get_settings() -> Settings:
    return Settings()
