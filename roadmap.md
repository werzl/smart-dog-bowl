# Roadmap

## Milestone 1 — Validate sensor (MVP foundation)

- [ ] Select ESP32-C3 board with MicroPython support and low idle current.
- [ ] Wire HX711 and load cell on USB power.
- [ ] Calibrate and persist calibration factor and empty-bowl tare.
- [ ] Capture 24h raw traces, including real drinking, refills, handling and spills.
- [ ] Verify platform repeatability and edge loading.

**Exit:** stable, repeatable readings and representative labelled traces.

## Milestone 2 — Ingest and store

- [ ] Scaffold Aspire AppHost, API, TimescaleDB and React PWA.
- [ ] Implement authenticated idempotent batch ingestion.
- [ ] Persist raw readings with UTC timestamps and sequence IDs.
- [ ] Add sample replay tool and API integration tests.

**Exit:** sensor traces visible from backend queries.

## Milestone 3 — Estimate consumption

- [ ] Implement plateau-based session detection.
- [ ] Distinguish refills, bowl removal and unknown disturbances.
- [ ] Store event confidence and classifier version.
- [ ] Compare calculated drinking against manually measured volumes.

**Exit:** daily totals are useful, with documented error and limitations.

## Milestone 4 — Battery and enclosure

- [ ] Integrate protected battery, charging and regulated supply.
- [ ] Measure current in active, idle, sleep and upload modes.
- [ ] Evaluate sleep scheduling versus short-session detection.
- [ ] Build splash-resistant raised stand with overload stops.
- [ ] Perform chew, splash, tip and thermal safety checks.

**Exit:** safe, repeatable, portable prototype with measured battery runtime.

## Milestone 5 — App and refinement

- [ ] Build daily, weekly and monthly dashboard.
- [ ] Add battery/connection status and refill history.
- [ ] Add configurable alerts for low bowl level and unusual consumption.
- [ ] Evaluate proximity-triggered sampling and Grafana dashboards.

## Deferred

Multiple bowls, multiple dogs (requires identity sensing), OTA firmware updates, cloud hosting, push notifications, automatic refill hardware, and advanced anomaly detection.
