#!/bin/bash
# auto topic creation is off, so create everything here
set -euo pipefail

BOOTSTRAP="${KAFKA_BOOTSTRAP:-kafka:9092}"
PARTITIONS="${PARTITIONS:-3}"
KAFKA_TOPICS=/opt/kafka/bin/kafka-topics.sh

topics=(
  shipments.created
  shipments.cancelled
  dispatch.courier-assigned
  tracking.location-updated
  tracking.status-changed
  eta.recalculated
  parcel-trace.healthchecks
)

# retry + dlq per consumer group
for group in shipments dispatch tracking eta; do
  topics+=("${group}.retry" "${group}.dlq")
done

for topic in "${topics[@]}"; do
  retention=604800000 # 7 days
  case "$topic" in
    tracking.location-updated) retention=86400000 ;; # 1 day
    *.dlq) retention=2592000000 ;;                   # 30 days
  esac
  $KAFKA_TOPICS --bootstrap-server "$BOOTSTRAP" --create --if-not-exists \
    --topic "$topic" --partitions "$PARTITIONS" --replication-factor 1 \
    --config retention.ms="$retention"
done

echo "Topics:"
$KAFKA_TOPICS --bootstrap-server "$BOOTSTRAP" --list
