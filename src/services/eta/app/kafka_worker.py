from __future__ import annotations

import json
import logging
import threading
import time

from confluent_kafka import Consumer, KafkaError, Message, Producer
from opentelemetry.trace import SpanKind

from .config import Settings
from .idempotency import ProcessedEvents
from .events import EVENT_ETA_RECALCULATED, TOPIC_ETA_RECALCULATED, TOPIC_LOCATION_UPDATED, envelope, recalculate
from .schemas import LocationUpdated
from .telemetry import extract_from_headers, inject_headers, tracer

log = logging.getLogger("eta.kafka")


def _header(msg: Message, key: str) -> str | None:
    for k, v in msg.headers() or []:
        if k == key and v is not None:
            return v.decode("utf-8")
    return None


class EtaWorker:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.retry_topic = f"{settings.kafka_group_id}.retry"
        self.dlq_topic = f"{settings.kafka_group_id}.dlq"
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self.running = False
        self.processed = 0
        self.processed_events = ProcessedEvents()
        self.consumer = Consumer(
            {
                "bootstrap.servers": settings.kafka_brokers,
                "group.id": settings.kafka_group_id,
                "enable.auto.commit": False,
                "auto.offset.reset": "earliest",
                "allow.auto.create.topics": False,
            }
        )
        self.producer = Producer({"bootstrap.servers": settings.kafka_brokers, "acks": "all", "enable.idempotence": True})

    def start(self) -> None:
        self._thread = threading.Thread(target=self._run, name="eta-kafka", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=10)
        self.producer.flush(5)

    def _run(self) -> None:
        topics = [TOPIC_LOCATION_UPDATED, self.retry_topic]
        self.consumer.subscribe(topics)
        self.running = True
        log.info("consuming %s", ", ".join(topics))
        try:
            while not self._stop.is_set():
                msg = self.consumer.poll(1.0)
                if msg is None:
                    continue
                if msg.error():
                    if msg.error().code() != KafkaError._PARTITION_EOF:
                        log.warning("consumer error: %s", msg.error())
                    continue
                self._process(msg)
                self.consumer.commit(message=msg, asynchronous=False)
        finally:
            self.running = False
            self.consumer.close()

    def _process(self, msg: Message) -> None:
        original_topic = _header(msg, "original-topic") or msg.topic()
        retry_count = int(_header(msg, "retry-count") or 0)
        try:
            event = json.loads(msg.value())
            update = LocationUpdated.model_validate(event["payload"])
        except Exception as exc:  # malformed: no point retrying
            log.error("poison message on %s: %s", msg.topic(), exc)
            self._forward(self.dlq_topic, msg, original_topic, retry_count, str(exc))
            return

        event_id = str(event.get("eventId", ""))
        if event_id and self.processed_events.seen(event_id):
            log.debug("event %s already processed", event_id)
            return

        ctx = extract_from_headers(msg.headers())
        with tracer.start_as_current_span(
            f"{original_topic} process",
            context=ctx,
            kind=SpanKind.CONSUMER,
            attributes={"messaging.system": "kafka", "messaging.source.name": original_topic, "messaging.message.id": event.get("eventId", "")},
        ):
            try:
                for result in recalculate(update, tz=self.settings.timezone):
                    self._publish(result)
                self.processed += 1
                if event_id:
                    self.processed_events.mark(event_id)
            except Exception as exc:
                attempt = retry_count + 1
                target = self.dlq_topic if attempt > self.settings.kafka_max_retries else self.retry_topic
                log.warning("handling failed (attempt %s), forwarding to %s: %s", attempt, target, exc)
                if target == self.retry_topic:
                    time.sleep(0.5 * attempt)
                self._forward(target, msg, original_topic, attempt, str(exc))

    def _publish(self, result) -> None:
        with tracer.start_as_current_span(
            f"{TOPIC_ETA_RECALCULATED} publish",
            kind=SpanKind.PRODUCER,
            attributes={"messaging.system": "kafka", "messaging.destination.name": TOPIC_ETA_RECALCULATED},
        ):
            headers = [("event-type", EVENT_ETA_RECALCULATED.encode())] + inject_headers()
            self.producer.produce(
                TOPIC_ETA_RECALCULATED,
                key=result.shipment_id,
                value=json.dumps(envelope(EVENT_ETA_RECALCULATED, result)),
                headers=headers,
            )
            self.producer.flush(10)
        log.info("eta %s: %s min", result.tracking_number, result.minutes_remaining, extra={"tracking_number": result.tracking_number})

    def _forward(self, topic: str, msg: Message, original_topic: str, retry_count: int, error: str) -> None:
        headers = [(k, v) for k, v in (msg.headers() or []) if k not in ("original-topic", "retry-count", "error")]
        headers += [
            ("original-topic", original_topic.encode()),
            ("retry-count", str(retry_count).encode()),
            ("error", error[:500].encode()),
        ]
        self.producer.produce(topic, key=msg.key(), value=msg.value(), headers=headers)
        self.producer.flush(10)
