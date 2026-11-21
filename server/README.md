# Local server

The server uses .NET 10, Aspire 17, ASP.NET Core, and PostgreSQL 17.6.
The AppHost starts the database and API. It also provides the Aspire dashboard.
The API applies versioned SQL migrations before it accepts requests.

## Start the server

Requirements: .NET 10 SDK, Node.js 18 or later, and Docker with Linux containers.
Aspire 17 also requires its CLI bundle. The AppHost can restore the pinned CLI through `dnx` on its first build.
See the [Aspire setup guide](https://aspire.dev/get-started/aspire-sdk/).

1. Start Docker.
2. Run this command from the repository root:

```powershell
dotnet run --project server/SmartBowl.AppHost
```

3. Open the dashboard URL from the terminal.
4. Open the API endpoint from the dashboard.

The API uses `http://localhost:5200` when it runs directly.
Aspire can assign a different port through its proxy. Use the API URL from the dashboard for requests.
The dashboard uses `http://localhost:15888` by default. The terminal provides its login link.

The database uses a Docker volume. Data remains after the AppHost stops.
Development mode seeds device `7e8c0d1a-6b39-47b5-928a-64f3f1039e21` with 1,441 readings across 24 hours.
The readings include small variations, weight decreases, and refills.
The seed runs only when this device GUID does not exist. Repeated startup preserves its readings and calibration.
Set `SeedData__Enabled=false` to disable the seed.

## Use an existing PostgreSQL instance

Set the connection string before you start the AppHost:

```powershell
$env:ConnectionStrings__bowldb = 'Host=localhost;Port=5432;Database=bowldb;Username=postgres;Password=YOUR_PASSWORD'
dotnet run --project server/SmartBowl.AppHost
```

This setting replaces the database container with an external connection.
The database must exist. The API creates its tables through migrations.
The database account must have permission to create tables.

For an API run without Aspire, use the same connection setting:

```powershell
dotnet run --project server/SmartBowl.Api
```

## Runtime on this computer

Docker was unavailable during setup. This computer currently uses a portable PostgreSQL instance in the ignored `.local/` directory.
The instance accepts connections only on `127.0.0.1:55432`. It uses the `postgres` database and local trust authentication.
The AppHost connects through `ConnectionStrings__bowldb`.

After a computer restart, start this instance from the repository root:

```powershell
& .\.local\postgres-runtime\bin\pg_ctl.exe -D .\.local\postgres-data -l .\.local\postgres.log -o '-h 127.0.0.1 -p 55432' -w start
```

Then start Aspire:

```powershell
$env:ConnectionStrings__bowldb = 'Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres'
$aspireTemp = Join-Path $PWD '.local/aspire-temp'
New-Item -ItemType Directory -Force $aspireTemp | Out-Null
$env:TEMP = $aspireTemp
$env:TMP = $aspireTemp
.\.tools\aspire.cmd run --apphost server/SmartBowl.AppHost/SmartBowl.AppHost.csproj --non-interactive
```

The local temporary directory avoids a missing-file error from the Aspire project launcher on this computer.
The `--non-interactive` option avoids the Windows certificate dialog. The local profile uses HTTP.
These files remain outside version control. Other computers can use the Docker setup or an existing PostgreSQL instance.

To stop Aspire, run this command:

```powershell
.\.tools\aspire.cmd stop --apphost server/SmartBowl.AppHost/SmartBowl.AppHost.csproj --non-interactive
```

To stop the portable database, run this command:

```powershell
& .\.local\postgres-runtime\bin\pg_ctl.exe -D .\.local\postgres-data -w stop
```

## Python values and DTOs

The existing Python files remain unchanged.
`SmartBowl.Contracts` contains the request and response DTOs.

| Python source | DTO value | Meaning |
|---|---|---|
| `calibrate.py` | `zero_offset` | Average raw reading with the empty bowl |
| `calibrate.py` | `calibration_factor` | Raw units per gram, with a nonzero positive or negative value |
| `weight_test.py` | `rawReading` | Optional average raw reading, which can contain a fraction |
| `weight_test.py` | `weightGrams` | `(raw - zero_offset) / factor`, with its sign preserved |

`CalibrationDto` uses the exact JSON keys from `calibration.json`.
Reading DTOs use camelCase keys to match the draft API in `software.md`.
The API stores the supplied weight without recalculation. Later calibration changes do not change stored weights.
The API accepts negative weights near zero because the Python calculation can produce them.
Raw readings and zero offsets must remain within the signed 24-bit range.

The Python scripts print measurements. They do not send HTTP requests.
The transport fields `sequence` and `observedAt` support deduplication and UTC storage.
`batchId` identifies a request in the response. Deduplication uses `(device_id, sequence)`.
The firmware must preserve each sequence across retries and restarts.
Register a new device ID if a sequence counter restarts from zero.

## Device IDs

Device IDs use .NET `Guid` values and PostgreSQL `uuid` columns.
Supply a nonempty GUID when you register a device. Swagger displays the `uuid` format for these values.
Malformed IDs return HTTP 400. An unknown valid GUID returns HTTP 404.

Migration `002_device_guids.sql` converts existing IDs and preserves device data and readings.
It keeps each old ID in `devices.legacy_device_id` for reference.
Existing nonempty GUIDs retain their value. Other old IDs receive new GUIDs.
The mock ID `bowl-01` becomes `7e8c0d1a-6b39-47b5-928a-64f3f1039e21`.
Use the GUID from `GET /api/v1/devices` in later requests.

## API

| Method | Path | Result |
|---|---|---|
| GET | `/api/v1/devices` | Devices and current calibration |
| POST | `/api/v1/devices` | Register a device |
| GET | `/api/v1/devices/{deviceId}/status` | Latest reading, calibration, and last upload time |
| GET | `/api/v1/devices/{deviceId}/calibration` | Current calibration |
| POST | `/api/v1/devices/{deviceId}/calibration` | Store calibration values |
| POST | `/api/v1/devices/{deviceId}/readings:batch` | Store readings and acknowledge sequence ranges |
| GET | `/api/v1/devices/{deviceId}/readings` | Readings in sequence order |
| GET | `/api/v1/devices/{deviceId}/drinking-summary` | Candidate drinking estimates for a time range |
| GET | `/health` | API and database health |
| GET | `/alive` | API process health |
| GET | `/openapi/v1.json` | OpenAPI document in Development mode |
| GET | `/swagger` | Swagger UI in Development mode |

Write requests require the `X-Device-Key` header.
The Development key is `local-development-key`.
Set `DeviceApiKey` through configuration for other environments. The API requires this setting at startup.
This first version uses one shared write key. Read endpoints do not require a key.
Use this setup locally. Per-device credentials and user authentication remain future work.

Open `http://localhost:5200/swagger` to test the API with Swagger UI.
If Aspire assigns a different port, use the API URL from the dashboard with `/swagger`.

1. Select **Authorize**.
2. Enter `local-development-key` for the Development setup.
3. Select **Authorize** in the dialog.
4. Close the dialog.
5. Expand an endpoint.
6. Select **Try it out**.
7. Enter the request values.
8. Select **Execute**.

Swagger UI sends the key as `X-Device-Key` for write requests. Read requests do not require a key.

Use `SmartBowl.Api/SmartBowl.Api.http` for sample requests.
Update its `baseUrl` to match the API endpoint.

Each batch accepts 1 to 1,000 readings with unique sequences.
The API acknowledges the batch only after the database transaction commits.
Retries with the same sequence and values return duplicate counts.
A retry with changed values returns HTTP 409 and inserts no readings from that batch.
Acknowledged ranges preserve gaps. The device must remove only acknowledged sequences from its queue.

Reading queries accept these parameters:

- `from`: inclusive timestamp.
- `to`: exclusive timestamp.
- `afterSequence`: exclusive sequence cursor, with a default of `-1`.
- `limit`: 1 to 1,000 readings, with a default of 100.

Use `nextAfterSequence` from a response as the next cursor. A null cursor means that no further page exists at query time.
The API stores timestamps in UTC. PostgreSQL stores timestamp values with microsecond precision.

## Tests

Run the contract tests:

```powershell
dotnet test tests/SmartBowl.Api.Tests
```

The database tests skip when `SMARTBOWL_TEST_DATABASE` has no value.
To run all tests, set a connection string for a local PostgreSQL database:

```powershell
$env:SMARTBOWL_TEST_DATABASE = 'Host=localhost;Port=5432;Database=bowldb;Username=postgres;Password=YOUR_PASSWORD'
dotnet test tests/SmartBowl.Api.Tests
```

The database tests create a separate schema with a random name. They remove that schema after the tests.
The test account must have permission to create and remove schemas.
Tests cover seed repetition, calibration, paging, validation, authentication, concurrent retries, and transaction rollback.

## Current scope

This setup stores raw measurements and calibration data.
Drinking analysis remains future work.
It uses standard PostgreSQL. TimescaleDB extensions and retention rules remain future work.
Wi-Fi credentials remain on the device. The server does not accept or store them.
