# Software design

## Device: MicroPython

Modules planned: `main.py` (scheduler), `hx711.py` (ADC driver), `calibration.py`, `sampling.py` (filter and plateau detection), `storage.py` (persistent queue), `transport.py` (HTTP batch client), `config.py` (secrets/settings).

Start with continuous sampling over USB to collect ground-truth traces. Later test timed wakeups or an external wake sensor. ESP32 deep sleep restarts MicroPython, so state and queued samples must be persisted. Use a robust write strategy to limit flash wear; avoid persisting every sample individually.

## API contract (draft)

`POST /api/v1/devices/{deviceId}/readings:batch`

```json
{
  "batchId": "bowl-01-00012",
  "readings": [
    {"sequence": 1201, "observedAt": "2026-10-17T12:00:00Z", "weightGrams": 850.2},
    {"sequence": 1202, "observedAt": "2026-10-17T12:00:01Z", "weightGrams": 849.9}
  ],
  "batteryMillivolts": 3890
}
```

Return acknowledged sequence ranges after durable insertion. Device retries must be idempotent. Additional endpoints:

- `GET /api/v1/devices/{deviceId}/status`
- `GET /api/v1/devices/{deviceId}/readings?from=&to=&bucket=`
- `GET /api/v1/devices/{deviceId}/sessions?from=&to=`
- `GET /api/v1/devices/{deviceId}/consumption?from=&to=&bucket=day`
- `POST /api/v1/devices/{deviceId}/calibration` (authenticated administrative action)

## Database (draft)

- `devices`: ID, name, bowl tare, calibration version, last seen, firmware version.
- `weight_readings`: device ID, sequence, observed UTC time, weight grams, quality flags; Timescale hypertable on timestamp.
- `events`: event ID, device ID, type (`drink_candidate`, `refill`, `bowl_removed`, `unknown`), start/end UTC, before/after weight, estimated ml, confidence, classifier version.
- `daily_consumption`: optional derived materialized view/continuous aggregate, generated from accepted events rather than raw downward sample differences.

Use Npgsql and SQL migrations. TimescaleDB enables time-bucket queries, retention and continuous aggregates; retain detailed readings long enough to tune the classifier, and define a deliberate retention policy.

## Session classification

State machine: `IDLE → CHANGING → SETTLING → CLASSIFY → IDLE`.

1. Apply median or trimmed-mean filtering to noisy readings.
2. Identify stable plateaus and candidate changes, not individual negative deltas.
3. Treat positive stable steps as refills, negative steps as possible consumption, and unstable/large disturbances as unknown.
4. Exclude bowl removal and replacement from consumption.
5. Record classifier version and confidence; support manual corrections and replay.

Starting thresholds such as a 5g minimum drop and 3–5 seconds settling must be validated with actual dog behaviour. If periodic sleep misses sessions, classify net losses conservatively and distinguish those estimates from confirmed sessions.

## React PWA

Vite + React + TypeScript, TanStack Query, Recharts. Pages: Overview (today's estimated intake, bowl level, battery), History (sessions and refills), Trends (daily/weekly/monthly), Device (connectivity/calibration), and Settings (thresholds, timezone). Backend owns aggregation and permissions; browser never accesses database credentials.

## Aspire

Suggested projects: `SmartBowl.AppHost`, `SmartBowl.ServiceDefaults`, `SmartBowl.Api`, `SmartBowl.Worker` (can initially be a hosted service inside API), `SmartBowl.Domain`, `SmartBowl.Infrastructure`, and `web/`. Register PostgreSQL/TimescaleDB container, API, worker and Vite frontend with AppHost. Confirm the selected Timescale image/extension and Aspire integration work together before locking package versions.
