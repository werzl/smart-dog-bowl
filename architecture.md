# Architecture

```text
Stainless bowl → isolated platform → 5kg load cell → HX711 → ESP32-C3 (MicroPython)
                                                        |
                                               Wi-Fi batch uploads
                                                        |
                                                ASP.NET Core API
                                                        |
                                           TimescaleDB (raw readings)
                                                        |
                                           .NET background classifier
                                                        |
                                           Drinking/refill/unknown events
                                                        |
                                               React + TypeScript PWA
```

.NET Aspire AppHost coordinates API, worker, database and frontend in development; Aspire is not itself the production hosting environment. A single API with a hosted background service is also acceptable for the MVP, avoiding premature service splitting.

## Data flow

1. Device samples and filters weights, stores an ordered sequence and buffers unsent readings.
2. Device uploads batches with device ID, monotonically increasing sequence numbers, sample timestamps (or elapsed-time metadata if wall-clock sync is unavailable), and battery diagnostics.
3. API authenticates the device, validates data, deduplicates by `(device_id, sequence)`, and stores raw samples.
4. Worker identifies stable plateaus and classifies negative changes as *candidate* drinking sessions, positive changes as refills, and ambiguous changes as unknown.
5. PWA queries API endpoints for sessions, consumption totals, time-bucketed trends and current device state.

## Design decisions

- **HTTP batch upload first:** simpler than MQTT for a sleeping device. Add MQTT only if low-latency subscriptions or additional devices warrant it.
- **Keep raw readings:** event classifications can be recalculated as algorithms evolve.
- **Separate event and measurement storage:** avoid conflating water lost with water drunk.
- **Store timestamps in UTC:** convert to the dog's local timezone for daily totals.
- **No direct browser-to-database access:** React talks to ASP.NET Core only.
- **Offline tolerance:** persist an unsent queue across reboots; acknowledge received sequence ranges before deletion.

## Security and deployment

For a home-network MVP, bind API to the local network and use a per-device secret. For internet-facing access use TLS, authentication, authorization, rate limits and secure secret provisioning. Never expose TimescaleDB directly to the internet. The PWA can be hosted behind a reverse proxy with the API. Back up the database regularly.
