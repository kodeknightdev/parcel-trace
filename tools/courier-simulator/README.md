# courier-simulator

Plays the couriers so the whole flow can be watched without a phone in hand. Each simulated courier
signs in through the gateway, goes on duty, answers the offers made by the dispatch assignment saga,
follows its route, streams location pings to the tracking service and reports pick-up, transit,
out-for-delivery and delivery (with a typed signature as proof).

No dependencies; needs Node 20 and the stack running (`docker compose ... up`).

```bash
cd tools/courier-simulator
node simulator.js                       # courier1-3; a ping every 3 s at 40 km/h, accept every offer
node simulator.js --speed 300 --interval 1 --once   # fast-forward and exit when the routes are empty
node simulator.js --reject-rate 0.5                 # reject half the offers: watch them move on and escalate
node simulator.js --couriers courier4@parceltrace.local   # a single courier (courier4 starts off duty)
```

| Option | Default | |
|---|---|---|
| `--gateway` | `$GATEWAY_URL` or `http://localhost:8080` | gateway base URL |
| `--password` | `$SEED_DEMO_PASSWORD` or `Demo@12345` | local demo password (from `.env`) |
| `--couriers` | courier1-3@parceltrace.local | comma-separated courier logins |
| `--interval` | `3` | seconds between pings |
| `--speed` | `40` | km/h; raise it to fast-forward |
| `--reject-rate` | `0` | share of offers to reject (0..1) |
| `--once` | off | exit after every courier has been idle for `--idle` ticks |
| `--idle` | `20` | idle ticks before `--once` exits |

Open the public tracking page (`http://localhost:8000/Track?tn=PT-…`) while it runs to see the
courier move on the map and the ETA update.
