# Smart Bowl UI

The local UI uses React, TypeScript, and Vite.
It takes visual ideas from smart home apps: a dark home view, clear device cards, and direct controls.
It reads device status and weight history from the ASP.NET Core API.

## Start with Aspire

Start the AppHost from the repository root:

```powershell
dotnet run --project server/SmartBowl.AppHost
```

The `web` resource serves the UI at `http://localhost:5173`.
Aspire starts the API before the UI. It passes the API address to the Vite proxy.
The browser uses relative `/api` URLs. It does not need direct database access.

See the [server guide](../server/README.md) for this computer's PostgreSQL and Aspire settings.

## Start the UI separately

Requirements: Node.js 18 or later and the API at `http://localhost:5200`.

Run these commands from `web/`:

```powershell
npm ci
npm run dev
```

Set `API_BASE_URL` before startup if the API uses another address.
Set `PORT` before startup if the UI needs another port.

## Current features

- Overview: latest bowl weight, last upload time, and measured weight range.
- History: weight chart, Drinking patterns card, time filters, paged readings, and CSV export.
- Devices: bowl selection, device GUIDs, and registration.
- Settings: device API key and calibration values.
- Layout: side navigation on desktop and bottom navigation on mobile.

Navigation uses links such as `#history` and `#devices`.
Breadcrumb links return to Overview. Browser Back and Forward restore the previous or next view. Reload keeps the current view.

The UI refreshes data every 30 seconds. The refresh button requests an immediate update.
The chart supports 24-hour and 7-day views.
Hover or tap the chart to see an exact weight and timestamp.
Focus the chart to use the Left and Right keys. Home selects the first reading. End selects the last reading.
The UI reports an upload as recent for five minutes after receipt.
The mock device stops receiving new samples after its first seed. Its upload status will therefore become stale.

Write actions require the device API key. The local Development key is `local-development-key`.
Save the key in Settings, or enter it when you add a bowl.
The UI keeps the saved key in `sessionStorage` for the current tab.
Each new bowl receives a GUID from `crypto.randomUUID()`.

The chart shows measured weights. The Drinking patterns card uses the server's preliminary detector to estimate consumption from candidate events.
The card follows the selected bowl and the 24-hour or 7-day range.
It shows estimated consumption, the busiest and quietest hours, and average duration when samples support it.
The event details list each candidate's estimated amount and duration.
The API excludes unobserved hours from quietest-time comparisons and reports equal hours as ties.

Duration requires readings at most two seconds apart. The minute-spaced mock samples therefore show an unavailable duration.
Spills can resemble drinks, so the card labels consumption as an estimate.
Battery levels remain unavailable.
Calibration changes preserve stored weights.

## Build

Run this command from `web/`:

```powershell
npm run build
```

The command checks TypeScript and creates a static build in `dist/`.
Production hosting remains future work. The manifest supports installation as a web app.

## Bowl notifications

Empty-bowl alerts appear in the app and reset after a refill.
Web Push supports background notifications in compatible browsers and installed web apps.
See the [notification guide](../server/NOTIFICATIONS.md) for mobile setup and empty/refill test requests.
