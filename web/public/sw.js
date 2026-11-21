/* The worker handles push messages. It does not cache API responses or device keys. */
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) =>
  event.waitUntil(self.clients.claim()),
);
self.addEventListener("push", (event) => {
  event.waitUntil(
    (async () => {
      let payload = {};
      try {
        const data = event.data?.json();
        payload = data && typeof data === "object" ? data : {};
      } catch {
        /* Show the fallback notification. */
      }
      let title = payload.title ?? "Smart Bowl notification";
      let body = payload.body ?? "Open Smart Bowl to check the water level.";
      try {
        const response = await fetch("/api/v1/notifications", {
          cache: "no-store",
          signal: AbortSignal.timeout(4000),
        });
        if (response.ok) {
          const items = await response.json();
          if (items.some((item) => item.id === payload.id && item.resolvedAt)) {
            title = "Bowl refilled";
            body = "The bowl was refilled after the empty-bowl alert.";
          }
        }
      } catch {
        /* Delivery can occur without access to the API. */
      }
      await self.registration.showNotification(title, {
        body,
        tag: `bowl-${payload.id ?? "alert"}`,
        icon: "/icon-192.png",
        badge: "/bowl.svg",
        timestamp: Date.parse(payload.createdAt) || Date.now(),
        data: { url: "/#overview" },
      });
      const windows = await self.clients.matchAll({
        type: "window",
        includeUncontrolled: true,
      });
      windows.forEach((client) =>
        client.postMessage({ type: "bowl-notification" }),
      );
    })(),
  );
});
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(
    (async () => {
      const windows = await self.clients.matchAll({
        type: "window",
        includeUncontrolled: true,
      });
      const client = windows.find(
        (window) => new URL(window.url).origin === self.location.origin,
      );
      if (client) {
        await client.navigate("/#overview");
        await client.focus();
      } else await self.clients.openWindow("/#overview");
    })(),
  );
});
