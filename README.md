# Smart Dog Bowl

A battery-powered, raised water-bowl stand that weighs water and estimates a dog's drinking sessions and daily water consumption.

> Status: design / prototype. Water leaving a bowl is not necessarily water swallowed: spills, evaporation and bowl handling can affect estimates.

## Goals

- Measure bowl weight using a load cell and HX711 ADC.
- Track drinking sessions, refills, battery level and device connectivity.
- Display daily, weekly and monthly trends in a mobile-friendly dashboard.
- Retain raw measurements so event-detection algorithms can be improved and historical data reprocessed.
- Operate from a rechargeable battery with safe, splash-resistant construction.

## Proposed stack

| Layer | Choice |
|---|---|
| Device | ESP32-C3, MicroPython, HX711 |
| Transport | HTTPS/HTTP batch ingestion over Wi-Fi (HTTPS for non-local traffic) |
| Backend | ASP.NET Core API + worker, orchestrated by .NET Aspire |
| Data | PostgreSQL + TimescaleDB extension |
| Frontend | React + TypeScript + Vite PWA |
| Observability | Aspire dashboard during development; optional Grafana |

See [architecture](architecture.md), [hardware](hardware.md), [software](software.md), and [roadmap](roadmap.md).

## Important engineering risks

1. **Battery life versus session accuracy:** periodic deep sleep can miss short drinks. Validate continuous readings first, then measure actual power consumption before selecting a wake strategy.
2. **Mechanical accuracy:** the platform must be supported *only* through the load cell, with no rubbing against the frame.
3. **False positives:** record unknown changes separately from drinking; do not assume every weight drop was consumed.
4. **Battery safety:** use a protected cell, appropriate charger with power-path management, and a regulator rated for Wi-Fi peaks. Keep all electronics inaccessible to the dog.

## First milestone

Produce stable calibrated weight readings over USB, log a full day of readings, and assess noise, drift, drinking signatures and refill detection before building the final enclosure.

## Local server

The ASP.NET Core API, PostgreSQL database, and React UI run through Aspire.
Development mode includes a mock bowl with 24 hours of readings.
The DTOs follow the working Python calibration and weight values in `src/`.

1. Start Docker with Linux containers.
2. Run `dotnet run --project server/SmartBowl.AppHost` from the repository root.
3. Open the dashboard URL from the terminal.

Use the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
See the [server guide](server/README.md) for API examples, database options, and tests.
The UI runs at `http://localhost:5173`. See the [UI guide](web/README.md) for its features.
The Python scripts remain unchanged.
