import { useEffect, useState } from "react";
import { Bell, BellOff, Check, Droplet, X } from "lucide-react";
import { requestApi } from "./api";

export interface BowlNotification {
  id: string;
  deviceId: string;
  deviceName: string;
  createdAt: string;
  resolvedAt: string | null;
  weightGrams: number;
}

export function useBowlNotifications() {
  const [items, setItems] = useState<BowlNotification[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    let pending = false;
    const refresh = async () => {
      if (pending) return;
      pending = true;
      try {
        const next = await requestApi<BowlNotification[]>("/notifications", {
          signal: controller.signal,
        });
        setItems(next);
        setError("");
        if ("serviceWorker" in navigator) {
          const registration = await navigator.serviceWorker.getRegistration();
          const shown = await registration?.getNotifications();
          for (const notification of shown ?? []) {
            if (
              next.some(
                (item) =>
                  `bowl-${item.id}` === notification.tag && item.resolvedAt,
              )
            )
              notification.close();
          }
        }
      } catch {
        if (!controller.signal.aborted)
          setError("Bowl alerts are unavailable. The app will try again.");
      } finally {
        pending = false;
      }
    };
    void refresh();
    const timer = setInterval(() => void refresh(), 5000);
    navigator.serviceWorker?.addEventListener("message", refresh);
    window.addEventListener("focus", refresh);
    return () => {
      controller.abort();
      clearInterval(timer);
      navigator.serviceWorker?.removeEventListener("message", refresh);
      window.removeEventListener("focus", refresh);
    };
  }, []);
  return { items, error };
}

export function NotificationBell({
  items,
  error,
}: {
  items: BowlNotification[];
  error: string;
}) {
  const [open, setOpen] = useState(false);
  const count = items.filter((item) => !item.resolvedAt).length;
  return (
    <div className="notification-menu">
      <button
        className="notification-bell"
        aria-label={`Notifications${count ? `, ${count} active` : ""}`}
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <Bell size={21} />
        {count > 0 && <span>{count}</span>}
      </button>
      {open && (
        <section className="notification-panel" aria-label="Bowl notifications">
          <div className="notification-panel-heading">
            <h2>Notifications</h2>
            <button
              className="icon-button"
              aria-label="Close notifications"
              onClick={() => setOpen(false)}
            >
              <X size={18} />
            </button>
          </div>
          {error && <p role="status">{error}</p>}
          {!error && items.length === 0 && <p>No empty-bowl alerts yet.</p>}
          {items.map((item) => (
            <article key={item.id} className="notification-entry">
              {item.resolvedAt ? <Check size={19} /> : <Droplet size={19} />}
              <div>
                <strong>
                  {item.resolvedAt ? "Bowl refilled" : "Bowl is empty"}
                </strong>
                <p>{item.deviceName}</p>
                <time dateTime={item.resolvedAt ?? item.createdAt}>
                  {new Date(item.resolvedAt ?? item.createdAt).toLocaleString(
                    "en-GB",
                    {
                      day: "numeric",
                      month: "short",
                      hour: "2-digit",
                      minute: "2-digit",
                    },
                  )}
                </time>
              </div>
            </article>
          ))}
        </section>
      )}
    </div>
  );
}

export function EmptyBowlAlerts({
  items,
  error,
  onSelect,
}: {
  items: BowlNotification[];
  error: string;
  onSelect: (id: string) => void;
}) {
  return (
    <div className="bowl-alerts" aria-live="polite">
      {error && <p className="notification-warning">{error}</p>}
      {items
        .filter((item) => !item.resolvedAt)
        .map((item) => (
          <div className="empty-bowl-alert" key={item.id}>
            <Droplet size={23} aria-hidden="true" />
            <div>
              <strong>{item.deviceName} is empty</strong>
              <p>Refill the bowl. This alert clears at 50 g or more.</p>
            </div>
            <a href="#overview" onClick={() => onSelect(item.deviceId)}>
              View bowl
            </a>
          </div>
        ))}
    </div>
  );
}

interface PushConfiguration {
  publicKey: string;
  configured: boolean;
}
const supported = () =>
  window.isSecureContext &&
  "serviceWorker" in navigator &&
  "PushManager" in window &&
  "Notification" in window;
const message = (error: unknown) =>
  error instanceof Error
    ? error.message
    : "The notification request failed. Try again.";
function applicationKey(value: string): Uint8Array<ArrayBuffer> {
  const bytes = atob(
    value
      .replace(/-/g, "+")
      .replace(/_/g, "/")
      .padEnd(Math.ceil(value.length / 4) * 4, "="),
  );
  return Uint8Array.from(bytes, (character) => character.charCodeAt(0));
}
function post(path: string, body: unknown, key: string) {
  return requestApi(`/notifications/push/${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Device-Key": key },
    body: JSON.stringify(body),
  });
}

export function PushSettings({ apiKey }: { apiKey: string }) {
  const [configuration, setConfiguration] = useState<PushConfiguration | null>(
    null,
  );
  const [active, setActive] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  useEffect(() => {
    let cancelled = false;
    requestApi<PushConfiguration>("/notifications/push/configuration")
      .then((value) => {
        if (!cancelled) setConfiguration(value);
      })
      .catch((error) => {
        if (!cancelled) setError(message(error));
      });
    if (supported())
      navigator.serviceWorker.ready
        .then((registration) => registration.pushManager.getSubscription())
        .then((subscription) => {
          if (!cancelled) setActive(!!subscription);
        })
        .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  async function enable() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      // Request permission directly from the button gesture, before network requests.
      const permission = await Notification.requestPermission();
      if (permission !== "granted")
        throw new Error(
          "Notifications are blocked. Allow notifications in your browser or device settings.",
        );
      const registration = await navigator.serviceWorker.ready;
      let subscription = await registration.pushManager.getSubscription();
      if (
        subscription &&
        configuration &&
        subscription.options.applicationServerKey
      ) {
        const existing = new Uint8Array(
          subscription.options.applicationServerKey,
        );
        const expected = applicationKey(configuration.publicKey);
        if (
          existing.length !== expected.length ||
          existing.some((value, index) => value !== expected[index])
        ) {
          await subscription.unsubscribe();
          subscription = null;
        }
      }
      subscription ??= await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: applicationKey(configuration!.publicKey),
      });
      await post("subscriptions", subscription.toJSON(), apiKey);
      setActive(true);
      setNotice("Empty-bowl notifications are enabled on this device.");
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  async function disable() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();
      if (subscription) {
        await post(
          "subscriptions/remove",
          { endpoint: subscription.endpoint },
          apiKey,
        );
        if (!(await subscription.unsubscribe()))
          throw new Error(
            "The browser could not remove the subscription. Try again.",
          );
      }
      setActive(false);
      setNotice("Notifications are disabled on this device.");
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="panel form-panel push-settings">
      <div className="card-title">
        <Bell size={22} />
        <div>
          <h2>Bowl notifications</h2>
          <p>Receive an alert when a bowl needs water.</p>
        </div>
      </div>
      <p>
        One alert per empty bowl. A refill resets the alert for the next time.
      </p>
      <p>
        Push notifications can arrive when the app is closed. Delivery depends
        on your browser, network, and device settings.
      </p>
      {!supported() && (
        <p className="notification-warning">
          Open the app over HTTPS. On iPhone or iPad, add it to the Home Screen,
          then open it there.
        </p>
      )}
      {configuration && !configuration.configured && (
        <p className="notification-warning">
          Push delivery needs a server contact address. Set WebPush:Subject
          before you enable notifications.
        </p>
      )}
      {!apiKey && (
        <p>Enter the device API key above to manage notifications.</p>
      )}
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      <div className="push-actions">
        <button
          className="button primary"
          disabled={
            busy || !supported() || !configuration?.configured || !apiKey
          }
          onClick={enable}
        >
          <Bell size={17} />
          {busy
            ? "Please wait…"
            : active
              ? "Check notification connection"
              : "Enable notifications on this device"}
        </button>
        {active && (
          <button
            className="button secondary"
            disabled={busy || !apiKey}
            onClick={disable}
          >
            <BellOff size={17} />
            Disable
          </button>
        )}
      </div>
      <small>
        iPhone and iPad require iOS or iPadOS 16.4 or later and a Home Screen
        installation.
      </small>
    </section>
  );
}
