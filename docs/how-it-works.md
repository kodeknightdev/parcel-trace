# How it works

This page follows a parcel from booking to delivery, then looks at what happens when things go
wrong. The service-level picture is in [architecture.md](architecture.md); the reasoning behind
each mechanism is in [design-patterns.md](design-patterns.md).

## 1. Booking a shipment

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant W as frontend
    participant G as gateway
    participant S as shipments
    participant E as eta
    participant K as Kafka
    participant D as dispatch
    participant T as tracking

    C->>W: fill in the booking form
    W->>G: POST /api/shipments (JWT from the cookie)
    G->>S: proxy
    S->>E: POST /api/eta/estimate (initial ETA)
    E-->>S: 62 min
    S->>S: insert shipment + outbox row (one transaction)
    S-->>C: 201 PT-2024-K7M2QX
    S--)K: shipments.created (outbox relay, ~1 s later)
    par
        K--)T: append Created to the timeline
    and
        K--)D: queue it, start the assignment saga
    end
```

The customer gets the tracking number before anything is on Kafka. The outbox relay publishes
the event shortly afterwards, and it can't get lost: it is in the same transaction as the
shipment row.

**Failure path: eta is down.** The shipments → eta client gives up after a 2 s attempt timeout
(or immediately if its circuit is open). The booking still succeeds without an initial ETA; the
first courier ping produces one.

## 2. Getting a courier: the assignment saga

Dispatch orchestrates assignment as a saga with its state in Mongo
([AssignmentService](../src/services/dispatch/ParcelTrace.Dispatch/Services/AssignmentService.cs),
[AssignmentSaga](../src/services/dispatch/ParcelTrace.Dispatch/Domain/AssignmentSaga.cs)).

```mermaid
sequenceDiagram
    autonumber
    participant D as dispatch (saga orchestrator)
    participant S as shipments
    participant M as MongoDB
    actor L as Courier Lena
    actor X as Courier Max
    participant K as Kafka

    D->>S: gRPC GetShipment (status, pick-up, weight)
    D->>M: hold Lena's capacity, saga = Offered(Lena, expires +2 min)
    L->>D: GET /api/dispatch/offers/me
    L->>D: POST /offers/{id}/accept
    D->>M: saga = Accepted, insert assignment
    D--)K: dispatch.courier-assigned
    Note over X: Max was never bothered
```

### Failure path: rejection, timeout, escalation

```mermaid
sequenceDiagram
    autonumber
    participant D as dispatch
    participant W as AssignmentSagaWorker (every 10 s)
    actor A as Courier A
    actor B as Courier B
    actor C as Courier C
    actor P as Dispatcher

    D->>A: offer (hold A's capacity)
    A->>D: reject "van is full"
    D->>D: release A, failures = 1
    D->>B: offer (hold B's capacity)
    W->>D: B's offer expired after 2 min
    D->>D: release B, failures = 2
    D->>C: offer (hold C's capacity)
    W->>D: C's offer expired
    D->>D: release C, failures = 3 → Escalated
    P->>D: POST /api/dispatch/assign (courier of choice)
    D->>D: saga = ManuallyAssigned, assignment created
```

Couriers are never asked twice for the same parcel. Rejections and timeouts count the same.
After three failures the shipment moves to the top of the dispatcher's queue with an
"Escalated" badge. If no courier is free at all, the saga stays `WaitingForCourier` and the
worker retries every 10 seconds. That isn't counted as a failure.

## 3. On the road: pings, ETAs and status updates

```mermaid
sequenceDiagram
    autonumber
    actor R as Courier
    participant G as gateway
    participant T as tracking
    participant K as Kafka
    participant E as eta
    participant S as shipments
    participant D as dispatch

    loop every few seconds
        R->>G: POST /api/tracking/pings
        G->>T: store in history, refresh cached position
        T--)K: tracking.location-updated (courier's open parcels)
        par
            K--)E: recalculate
            E--)K: eta.recalculated
            K--)T: ETA store + cache
            K--)S: ETA (delay detection)
        and
            K--)D: courier's last position (for the next offers)
        end
    end
    R->>G: POST /api/tracking/shipments/{id}/status PickedUp
    T->>T: append PickedUp to the log, commit after Kafka ack
    T--)K: tracking.status-changed
    K--)S: status PickedUp
    R->>G: … OutForDelivery, then Delivered + proof
    T--)K: tracking.status-changed Delivered
    K--)S: Delivered, proof stored
    K--)D: assignment completed, courier freed
```

**Failure path: events out of order.** If `PickedUp` reaches shipments before
`dispatch.courier-assigned` does (they travel on different topics), the shipment state machine
rejects it. The consumer forwards the message to `shipments.retry` and processes it again a
moment later, by which time the assignment has landed. After five failed attempts a message is
parked on `shipments.dlq`.

## 4. Cancelling after a courier was assigned (compensation)

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant S as shipments
    participant K as Kafka
    participant D as dispatch
    participant T as tracking

    C->>S: POST /api/shipments/{id}/cancel (within 30 min, before pick-up)
    S->>S: status Cancelled + outbox row (one transaction)
    S--)K: shipments.cancelled
    par compensation in dispatch
        K--)D: end the saga (withdraw an open offer), close the assignment
        D->>D: release the courier's capacity
    and compensation in tracking
        K--)T: append Cancelled, closing the timeline
        T->>T: projection + board updated
    end
```

If a courier accepts an offer at the same moment the customer cancels, both paths are safe.
The saga is saved with an optimistic version check, so only one transition wins. Any
`courier-assigned` that does slip through is ignored by shipments and tracking, because the
shipment is already terminal.

## 5. Following a parcel (public page)

```mermaid
sequenceDiagram
    autonumber
    actor P as Anyone
    participant W as frontend
    participant G as gateway (composition)
    participant S as shipments
    participant T as tracking
    participant E as eta

    P->>W: /Track?tn=PT-2024-K7M2QX
    W->>G: GET /api/track/PT-2024-K7M2QX
    par
        G->>S: GET /api/shipments/track/{tn}
    and
        G->>T: GET /api/tracking/{tn}
    end
    G->>E: POST /api/eta/estimate from the courier's last position
    G-->>W: merged view + sources {shipments, tracking, eta}
    loop every 15 s
        W->>G: refresh (moves the courier marker, updates the ETA)
    end
```

**Failure path: a source is down.** With tracking down, the page shows the booking facts from
shipments and the booking-time ETA, with an empty timeline. With eta down, it shows the last
recalculated ETA. The `sources` field tells the page what is missing, and it says so in a note.

## State diagrams

### Shipment

Enforced by [ShipmentStateMachine.cs](../src/services/shipments/ParcelTrace.Shipments/Domain/ShipmentStateMachine.cs)
and [stateMachine.js](../src/services/tracking/src/stateMachine.js).

```mermaid
stateDiagram-v2
    [*] --> Created: customer books
    Created --> Assigned: courier accepts / dispatcher assigns
    Created --> Cancelled: customer, within window
    Assigned --> Assigned: re-assigned before pick-up
    Assigned --> Cancelled: customer, within window
    Assigned --> PickedUp
    Assigned --> Failed
    PickedUp --> InTransit
    PickedUp --> OutForDelivery
    PickedUp --> Failed
    InTransit --> OutForDelivery
    InTransit --> Failed
    OutForDelivery --> Delivered: proof of delivery
    OutForDelivery --> Failed: reason
    Delivered --> [*]
    Failed --> [*]
    Cancelled --> [*]
```

### Assignment saga

```mermaid
stateDiagram-v2
    [*] --> WaitingForCourier: shipments.created
    WaitingForCourier --> Offered: nearest untried courier, capacity held
    WaitingForCourier --> WaitingForCourier: nobody free, retry in 10 s
    Offered --> Accepted: courier accepts in time
    Offered --> WaitingForCourier: rejected or timed out (failures < 3)
    Offered --> Escalated: rejected or timed out (failures = 3)
    Escalated --> ManuallyAssigned: dispatcher assigns
    WaitingForCourier --> ManuallyAssigned: dispatcher assigns
    Offered --> ManuallyAssigned: dispatcher assigns (offer withdrawn)
    WaitingForCourier --> Cancelled: shipments.cancelled
    Offered --> Cancelled: shipments.cancelled (offer withdrawn)
    Escalated --> Cancelled: shipments.cancelled
    Accepted --> [*]
    ManuallyAssigned --> [*]
    Cancelled --> [*]
```
