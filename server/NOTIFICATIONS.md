# Bowl notifications

The API creates an alert when a new reading is between -20 g and 10 g, inclusive.
A reading of at least 50 g clears the alert and resets detection.
Values between 10 g and 50 g retain the current state. This prevents repeated alerts near zero.
Values below -20 g can indicate a removed bowl. These values do not create or clear an alert.
These thresholds assume that calibration sets the empty bowl to zero.

The API stores alerts, detection state, browser subscriptions, push keys, and pending deliveries in PostgreSQL.
The reading and its alert commit in one transaction.
Duplicate uploads do not create duplicate alerts. Conflicting uploads roll back the complete batch.
The detector processes new readings in timestamp and sequence order.
Readings older than the latest processed reading do not change the alert state.
Readings outside the previous five minutes or more than one minute ahead do not change the alert state.
Historical uploads therefore do not send old empty-bowl alerts.

The app checks for alerts every five seconds. The bell shows active alerts and recent alert history.
The banner clears after a refill. An open app also closes its push notification after the refill.

## Test with the existing readings endpoint

Open [the local app](http://localhost:5173). Use the mock bowl for this test.
Run these commands separately from the repository root:

```powershell
# Empty the bowl. Expect activeAlerts = 1.
.\server\Test-BowlNotification.ps1 -Action Empty

# Refill the bowl. Expect activeAlerts = 0.
.\server\Test-BowlNotification.ps1 -Action Refill
```

Keep the bowl empty until the notification arrives. An immediate refill cancels a push that the server did not send yet.
Repeat the empty command after a refill to create another alert.
The commands insert real mock readings. They do not remove existing data.
Use `-DeviceId`, `-BaseUrl`, or `-DeviceKey` to select another test device or server.
Use `-RefillGrams 900.2` to restore the original mock weight.

The script calls the existing endpoint:

```http
POST /api/v1/devices/7e8c0d1a-6b39-47b5-928a-64f3f1039e21/readings:batch
Content-Type: application/json
X-Device-Key: local-development-key

{
  "batchId": "empty-test-UNIQUE_SEQUENCE",
  "readings": [{
    "sequence": 1234567890,
    "observedAt": "REPLACE_WITH_CURRENT_UTC_TIMESTAMP",
    "weightGrams": 0
  }]
}
```

For a refill, use a new sequence, a new timestamp, and `"weightGrams": 500`.
The script supplies fresh timestamps, unused sequences, and calibrated raw values automatically.
The example placeholders require replacement before a manual request.
Swagger exposes the same endpoint at [Swagger UI](http://localhost:5200/swagger).

## Enable push delivery

1. Set a real contact address for the push service:

```powershell
dotnet user-secrets set "WebPush:Subject" "mailto:YOUR_EMAIL_ADDRESS" --project server/SmartBowl.Api
```

2. Restart the API through Aspire.
3. Open Settings in the app.
4. Save the device API key.
5. Select **Enable notifications on this device**.
6. Allow notifications when the browser requests permission.
7. Run the empty test.
8. Close the app tab to test background delivery.
9. Run the refill test after the alert arrives.

The API generates VAPID keys once and retains them in the database.
Do not rotate or remove these keys without renewing browser subscriptions.
Protect database access and backups because they contain the private key and subscription credentials.
For a deployed server, set `WebPush__Subject` through its secret or environment configuration.
The subject can also be a public HTTPS contact URL.

Localhost supports desktop testing. A phone needs a reachable HTTPS origin that serves the UI and proxies `/api` to this API.
The phone cannot use the PC's localhost address. Plain HTTP on the PC's LAN address does not support Web Push.
Do not expose the PostgreSQL port to the phone.

On iPhone or iPad, install the app with **Share > Add to Home Screen**.
Open that installation before you enable notifications. iOS and iPadOS require version 16.4 or later.
See [WebKit's mobile push requirements](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/).
Android browsers that support Web Push can receive alerts after permission.
The manifest and service worker support an installed web app. This repository does not contain a native mobile app.
A future native app needs an APNs or FCM transport. It can reuse the stored alerts and delivery trigger.

## Delivery behavior

The worker sends encrypted `aes128gcm` messages with VAPID authentication.
It uses [Lib.Net.Http.WebPush](https://github.com/tpeczek/Lib.Net.Http.WebPush).
The server accepts Apple, Google, Mozilla, and Microsoft push endpoints. It rejects arbitrary URLs and redirects.
Subscription changes require `X-Device-Key`. Each subscription receives alerts for all bowls on this server.
The current local API uses one shared key. It does not provide separate household accounts.

Pending deliveries survive an API restart. The worker retries failed requests with increasing delays.
It removes subscriptions when the push service returns HTTP 404 or 410.
A refill cancels pending deliveries. Alerts older than five minutes expire from the delivery queue.
Push messages expire at the provider after 60 seconds.
The operating system can delay or block delivery because of permissions, focus settings, network access, or browser restrictions.
Delivery uses at-least-once processing. A crash after provider acceptance can cause a retry.
The service worker uses an alert ID as the notification tag to replace duplicate shows.

The service worker checks the alert state when a message arrives.
If the API reports a refill, it shows **Bowl refilled** instead of an empty-bowl message.
If the API is unreachable, it shows the original report with its timestamp.
A provider can deliver an accepted message after a refill. The server cannot recall that message.

API endpoints:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/v1/notifications` | Active alerts and recent history, up to 100 entries |
| GET | `/api/v1/notifications/push/configuration` | Public VAPID key and setup status |
| POST | `/api/v1/notifications/push/subscriptions` | Add or renew a browser subscription |
| POST | `/api/v1/notifications/push/subscriptions/remove` | Remove a browser subscription by endpoint |

Push delivery needs a real device subscription and permission for a complete device test.
Automated tests check state changes, persistence, retries, cancellation, and encrypted transport with a simulated push service.
