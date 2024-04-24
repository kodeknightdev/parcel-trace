CREATE TABLE IF NOT EXISTS shipment_events (
    id               bigserial PRIMARY KEY,
    shipment_id      uuid NOT NULL,
    sequence         integer NOT NULL,
    type             varchar(30) NOT NULL,
    data             jsonb NOT NULL DEFAULT '{}'::jsonb,
    source           varchar(20) NOT NULL,
    -- kafka event id, unique so redeliveries don't append twice
    source_event_id  uuid NULL UNIQUE,
    occurred_at      timestamptz NOT NULL,
    recorded_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_shipment_events_sequence UNIQUE (shipment_id, sequence)
);

-- append-only
CREATE OR REPLACE FUNCTION shipment_events_append_only() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'shipment_events is append-only (% not allowed)', TG_OP;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_shipment_events_append_only ON shipment_events;
CREATE TRIGGER trg_shipment_events_append_only
    BEFORE UPDATE OR DELETE ON shipment_events
    FOR EACH ROW EXECUTE FUNCTION shipment_events_append_only();

CREATE TABLE IF NOT EXISTS shipment_view (
    shipment_id      uuid PRIMARY KEY,
    tracking_number  varchar(20) NOT NULL UNIQUE,
    customer_id      uuid NOT NULL,
    service_level    varchar(20) NOT NULL,
    status           varchar(20) NOT NULL,
    courier_id       uuid NULL,
    courier_name     varchar(120) NULL,
    pickup_city      varchar(100) NOT NULL,
    pickup_lat       double precision NOT NULL,
    pickup_lng       double precision NOT NULL,
    dropoff_city     varchar(100) NOT NULL,
    dropoff_postal   varchar(20) NOT NULL,
    dropoff_lat      double precision NOT NULL,
    dropoff_lng      double precision NOT NULL,
    weight_kg        numeric(6,2) NOT NULL,
    sla_due_at       timestamptz NOT NULL,
    created_at       timestamptz NOT NULL,
    delivered_to     varchar(120) NULL,
    delivered_at     timestamptz NULL,
    last_sequence    integer NOT NULL,
    updated_at       timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_shipment_view_courier_status ON shipment_view (courier_id, status);

CREATE TABLE IF NOT EXISTS eta_estimates (
    shipment_id       uuid PRIMARY KEY,
    eta_at            timestamptz NOT NULL,
    minutes_remaining integer NULL,
    distance_km       double precision NULL,
    calculated_at     timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS location_pings (
    id           bigserial PRIMARY KEY,
    courier_id   uuid NOT NULL,
    lat          double precision NOT NULL,
    lng          double precision NOT NULL,
    speed_kmh    double precision NULL,
    recorded_at  timestamptz NOT NULL,
    received_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_location_pings_courier ON location_pings (courier_id, recorded_at DESC);

CREATE TABLE IF NOT EXISTS processed_events (
    event_id     uuid NOT NULL,
    consumer     varchar(50) NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (event_id, consumer)
);
