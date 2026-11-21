import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import {
  Activity,
  Check,
  ChevronDown,
  Clock3,
  Copy,
  Download,
  Droplet,
  Gauge,
  Droplets,
  History,
  House,
  PawPrint,
  Plus,
  RefreshCw,
  Settings,
  SlidersHorizontal,
  Waves,
  X,
} from "lucide-react";
import { getReadings, request, write } from "./api";
import type { Calibration, Device, Reading, Status } from "./api";
import WaterChart from "./WaterChart";
import DrinkingPatterns from "./DrinkingPatterns";

type View = "Overview" | "History" | "Devices" | "Settings";
const navigation = [
  { name: "Overview", icon: House },
  { name: "History", icon: History },
  { name: "Devices", icon: Droplets },
  { name: "Settings", icon: Settings },
] as const;
function readView(): View {
  return (
    navigation.find(
      ({ name }) =>
        `#${name.toLowerCase()}` === window.location.hash.toLowerCase(),
    )?.name ?? "Overview"
  );
}
function navigate(view: View) {
  window.location.hash = view.toLowerCase();
}
const number = new Intl.NumberFormat("en-GB", { maximumFractionDigits: 1 });
const dateTime = (value: string) =>
  new Date(value).toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
function ago(value: string | null) {
  if (!value) return "No uploads yet";
  const minutes = Math.max(
    0,
    Math.floor((Date.now() - Date.parse(value)) / 60_000),
  );
  if (minutes < 1) return "Just now";
  if (minutes < 60) return `${minutes} min ago`;
  if (minutes < 1440) return `${Math.floor(minutes / 60)} hr ago`;
  return `${Math.floor(minutes / 1440)} days ago`;
}
const errorText = (error: unknown) =>
  error instanceof Error ? error.message : "The request failed. Try again.";
const isRecent = (status: Status) =>
  !!status.lastSeenAt && Date.now() - Date.parse(status.lastSeenAt) < 300_000;

export default function App() {
  const [view, setView] = useState<View>(readView);
  const [statuses, setStatuses] = useState<Status[]>([]);
  const [selectedId, setSelectedId] = useState(
    localStorage.getItem("bowl-selected") ?? "",
  );
  const [readings, setReadings] = useState<Reading[]>([]);
  const [days, setDays] = useState(1);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [chartLoading, setChartLoading] = useState(false);
  const readingScope = useRef("");
  const [error, setError] = useState("");
  const [readingError, setReadingError] = useState("");
  const [showAdd, setShowAdd] = useState(false);
  const [toast, setToast] = useState("");
  const [apiKey, setApiKey] = useState(
    sessionStorage.getItem("bowl-device-key") ?? "",
  );
  const selected = statuses.find((device) => device.deviceId === selectedId);
  const bounds = readings.reduce(
    (range, reading) => ({
      min: Math.min(range.min, reading.weightGrams),
      max: Math.max(range.max, reading.weightGrams),
    }),
    { min: Infinity, max: -Infinity },
  );
  const refresh = useCallback(() => setRevision((value) => value + 1), []);
  const selectDevice = (id: string) => {
    setSelectedId(id);
    localStorage.setItem("bowl-selected", id);
  };
  const notify = (message: string) => setToast(message);

  useEffect(() => {
    const handleNavigation = () => setView(readView());
    window.addEventListener("hashchange", handleNavigation);
    return () => window.removeEventListener("hashchange", handleNavigation);
  }, []);
  useEffect(() => {
    document.title = `Smart Bowl · ${view}`;
    window.scrollTo({ top: 0 });
  }, [view]);
  useEffect(() => {
    const interval = setInterval(refresh, 30_000);
    return () => clearInterval(interval);
  }, [refresh]);
  useEffect(() => {
    if (!toast) return;
    const timeout = setTimeout(() => setToast(""), 4000);
    return () => clearTimeout(timeout);
  }, [toast]);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    request<Device[]>("", { signal: controller.signal })
      .then((devices) =>
        Promise.all(
          devices.map((device) =>
            request<Status>(`/${device.deviceId}/status`, {
              signal: controller.signal,
            }),
          ),
        ),
      )
      .then((devices) => {
        setStatuses(devices);
        setError("");
        setSelectedId((previous) =>
          devices.some((device) => device.deviceId === previous)
            ? previous
            : (devices[0]?.deviceId ?? ""),
        );
      })
      .catch((error) => {
        if (!controller.signal.aborted) setError(errorText(error));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [revision]);
  useEffect(() => {
    if (!selectedId) {
      setReadings([]);
      return;
    }
    const controller = new AbortController();
    setChartLoading(true);
    setReadingError("");
    const scope = `${selectedId}-${days}`;
    if (readingScope.current !== scope) {
      setReadings([]);
      readingScope.current = scope;
    }
    getReadings(selectedId, days, controller.signal)
      .then(setReadings)
      .catch((error) => {
        if (!controller.signal.aborted) setReadingError(errorText(error));
      })
      .finally(() => {
        if (!controller.signal.aborted) setChartLoading(false);
      });
    return () => controller.abort();
  }, [selectedId, days, revision]);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a href="#overview" className="brand" aria-label="Smart Bowl overview">
          <span className="brand-symbol">
            <PawPrint size={23} strokeWidth={1.9} aria-hidden="true" />
          </span>
          <span>
            smart<span className="brand-light">bowl</span>
          </span>
        </a>

        <p className="nav-label">YOUR SPACE</p>
        <nav aria-label="Main navigation">
          {navigation.map(({ name, icon: Icon }) => (
            <a
              key={name}
              href={`#${name.toLowerCase()}`}
              className={`nav-item ${view === name ? "active" : ""}`}
              aria-current={view === name ? "page" : undefined}
            >
              <Icon size={20} aria-hidden="true" />
              <span>{name}</span>
              {view === name && <span className="nav-mark" />}
            </a>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="server-status">
            <span className={`status-dot ${error ? "amber" : ""}`} />
            {error ? "Server unavailable" : "Local server connected"}
          </div>
        </div>
      </aside>
      <main>
        <header className="topbar">
          <nav className="breadcrumb" aria-label="Breadcrumb">
            <a href="#overview" title="Back to overview">
              My home
            </a>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{view}</span>
          </nav>
          <div className="topbar-right">
            <span className="today">
              {new Date().toLocaleDateString("en-GB", {
                weekday: "short",
                day: "numeric",
                month: "long",
              })}
            </span>
            <span className="avatar" aria-label="Adam">
              A
            </span>
          </div>
        </header>
        <div className="page-body">
          <div className="page-heading">
            <div>
              <p className="eyebrow">
                {view === "Overview"
                  ? "EVERYDAY CARE FOR YOUR DOG"
                  : "YOUR SMART BOWL"}
              </p>

              <h1>
                {view === "Overview"
                  ? "Welcome home."
                  : view === "History"
                    ? "The day, in detail."
                    : view === "Devices"
                      ? "Your bowls."
                      : "Just the way you like it."}
              </h1>

              {view !== "Overview" && (
                <p className="subtitle">
                  {view === "History"
                    ? "Measured weights from your bowl, in one place."
                    : view === "Devices"
                      ? "Check your bowls and their latest readings."
                      : "Manage access and the calibration of your bowl."}
                </p>
              )}
            </div>
            <div className="heading-actions">
              <button
                className="icon-button refresh"
                aria-label="Refresh readings"
                title="Refresh readings"
                onClick={refresh}
                disabled={loading || chartLoading}
              >
                <RefreshCw size={19} className={loading ? "spin" : ""} />
              </button>
              <button
                className="button primary"
                onClick={() => setShowAdd(true)}
              >
                <Plus size={18} />
                Add a bowl
              </button>
            </div>
          </div>
          {(error || readingError) && (
            <div role="alert" className="error-banner">
              <Activity size={18} />
              <span>{error || readingError}</span>
              <button onClick={refresh}>Try again</button>
            </div>
          )}
          {!loading && !error && statuses.length === 0 ? (
            <section className="empty-state">
              <Droplets size={45} />
              <h2>A home for your first bowl.</h2>
              <p>Add a bowl to see its weight readings here.</p>
              <button
                className="button primary"
                onClick={() => setShowAdd(true)}
              >
                <Plus size={18} />
                Add a bowl
              </button>
            </section>
          ) : (
            <>
              {view === "Overview" && (
                <>
                  <section className="overview-top">
                    <div className="water-card">
                      <div className="water-card-top">
                        <span className="hero-label">
                          <Droplets size={17} />
                          WATER IN THE BOWL
                        </span>
                        <span className="sample-badge">
                          {selected?.name.toLowerCase().includes("mock")
                            ? "Sample device"
                            : "Latest reading"}
                        </span>
                      </div>
                      <div className="water-card-content">
                        <div className="water-info">
                          <h2>{selected?.name ?? "Your bowl"}</h2>
                          <div className="water-value">
                            {selected?.latestReading
                              ? number.format(
                                  selected.latestReading.weightGrams,
                                )
                              : "—"}
                            <span>g</span>
                          </div>
                          <p>Latest measured weight</p>
                          <button
                            className="button glass"
                            onClick={() => navigate("History")}
                          >
                            View readings
                            <History size={16} />
                          </button>
                        </div>
                        <div
                          className="bowl-scene"
                          role="img"
                          aria-label="Illustration of a bowl of water"
                        >
                          <div className="bowl-halo" />
                          <div className="water-drop drop-one" />
                          <div className="water-drop drop-two" />
                          <div className="bowl-shadow" />
                          <div className="bowl-body">
                            <PawPrint className="bowl-paw" aria-hidden="true" />
                          </div>
                          <div className="bowl-rim">
                            <div className="bowl-water">
                              <span />
                              <span />
                            </div>
                          </div>
                          <div className="bowl-foot" />
                        </div>
                      </div>
                      <div className="hero-footer">
                        <span>
                          <span
                            className={`status-dot ${selected && isRecent(selected) ? "" : "amber"}`}
                          />
                          {selected
                            ? isRecent(selected)
                              ? "Recent upload"
                              : "No recent upload"
                            : "Loading device"}
                        </span>
                        <span>
                          {selected?.latestReading
                            ? dateTime(selected.latestReading.observedAt)
                            : "Waiting for a reading"}
                        </span>
                      </div>
                    </div>
                    <div className="summary-card panel">
                      <div className="section-title">
                        <span className="small-icon">
                          <Gauge size={18} aria-hidden="true" />
                        </span>
                        <h2>At a glance</h2>
                      </div>
                      <Summary
                        icon={<Clock3 size={19} />}
                        label="Last upload"
                        value={selected ? ago(selected.lastSeenAt) : "—"}
                      />
                      <Summary
                        icon={<Waves size={19} />}
                        label="Readings in view"
                        value={
                          chartLoading
                            ? "Loading"
                            : number.format(readings.length)
                        }
                      />
                      <Summary
                        icon={<SlidersHorizontal size={19} />}
                        label="Observed range"
                        value={
                          readings.length
                            ? `${number.format(bounds.min)}–${number.format(bounds.max)} g`
                            : "—"
                        }
                      />
                      <p className="summary-note">
                        A weight change can include a drink, a refill, a spill,
                        or bowl handling.
                      </p>
                    </div>
                  </section>
                  <section className="overview-bottom">
                    <div className="panel chart-panel">
                      <div className="panel-heading">
                        <div>
                          <h2>Water history</h2>
                          <p>Measured weight over time</p>
                        </div>
                        <Range days={days} setDays={setDays} />
                      </div>
                      <WaterChart
                        readings={readings}
                        loading={chartLoading}
                        days={days}
                        error={readingError}
                      />
                    </div>
                    <div className="devices-panel">
                      <div className="panel-heading">
                        <h2>
                          Your bowls{" "}
                          <span className="count">{statuses.length}</span>
                        </h2>
                        <button
                          className="text-button"
                          onClick={() => navigate("Devices")}
                        >
                          View all
                        </button>
                      </div>
                      {statuses.map((device) => (
                        <DeviceCard
                          key={device.deviceId}
                          device={device}
                          selected={device.deviceId === selectedId}
                          onClick={() => selectDevice(device.deviceId)}
                        />
                      ))}
                      <button
                        className="add-tile"
                        onClick={() => setShowAdd(true)}
                      >
                        <Plus size={18} />
                        Add another bowl
                      </button>
                      <div className="quiet-note">
                        <PawPrint size={20} />
                        <p>
                          Small details.
                          <br />
                          <strong>Everyday care.</strong>
                        </p>
                      </div>
                    </div>
                  </section>
                </>
              )}
              {view === "History" && (
                <section className="panel history-panel">
                  <div className="panel-heading">
                    <div>
                      <h2>{selected?.name ?? "Your bowl"}</h2>
                      <p>All values are measured in grams.</p>
                    </div>
                    <div className="history-tools">
                      <DeviceSelect
                        statuses={statuses}
                        selectedId={selectedId}
                        onChange={selectDevice}
                      />
                      <Range days={days} setDays={setDays} />
                    </div>
                  </div>
                  <DrinkingPatterns
                    key={`patterns-${selectedId}-${days}`}
                    deviceId={selectedId}
                    days={days}
                    revision={revision}
                    onRetry={refresh}
                  />
                  <WaterChart
                    readings={readings}
                    loading={chartLoading}
                    days={days}
                    error={readingError}
                  />
                  <ReadingsTable
                    key={`${selectedId}-${days}`}
                    readings={readings}
                    loading={chartLoading}
                  />
                </section>
              )}
              {view === "Devices" && (
                <>
                  <div className="device-grid">
                    {statuses.map((device) => (
                      <div
                        key={device.deviceId}
                        className="panel device-detail"
                      >
                        <DeviceCard
                          device={device}
                          selected={device.deviceId === selectedId}
                          onClick={() => {
                            selectDevice(device.deviceId);
                            navigate("Overview");
                          }}
                        />
                        <dl>
                          <div>
                            <dt>Last upload</dt>
                            <dd>{ago(device.lastSeenAt)}</dd>
                          </div>
                          <div>
                            <dt>Latest reading</dt>
                            <dd>
                              {device.latestReading
                                ? `${number.format(device.latestReading.weightGrams)} g`
                                : "No readings yet"}
                            </dd>
                          </div>
                        </dl>
                        <div className="id-row">
                          <code>{device.deviceId}</code>
                          <button
                            className="icon-button"
                            aria-label={`Copy ID for ${device.name}`}
                            onClick={() => {
                              navigator.clipboard
                                .writeText(device.deviceId)
                                .then(() => notify("Device ID copied."))
                                .catch(() =>
                                  notify("The device ID could not be copied."),
                                );
                            }}
                          >
                            <Copy size={16} />
                          </button>
                        </div>
                        <button
                          className="button secondary wide"
                          onClick={() => {
                            selectDevice(device.deviceId);
                            navigate("Settings");
                          }}
                        >
                          Manage calibration
                        </button>
                      </div>
                    ))}
                  </div>
                </>
              )}
              {view === "Settings" && (
                <div className="settings-grid">
                  <AccessSettings
                    apiKey={apiKey}
                    onSave={(key) => {
                      setApiKey(key);
                      if (key) sessionStorage.setItem("bowl-device-key", key);
                      else sessionStorage.removeItem("bowl-device-key");
                      notify(
                        key
                          ? "Device key saved for this tab."
                          : "Device key removed.",
                      );
                    }}
                  />
                  <CalibrationSettings
                    key={selectedId}
                    selected={selected}
                    statuses={statuses}
                    onSelect={selectDevice}
                    apiKey={apiKey}
                    onSaved={() => {
                      refresh();
                      notify("Calibration saved.");
                    }}
                  />
                </div>
              )}
            </>
          )}
          <footer className="page-footer">
            <span>
              <Droplets size={14} />A little care, every day.
            </span>
            <span>
              Weights show water in the bowl. They do not measure water
              swallowed.
            </span>
          </footer>
        </div>
      </main>
      {toast && (
        <div role="status" className="toast">
          <Check size={17} />
          {toast}
        </div>
      )}
      {showAdd && (
        <AddDevice
          apiKey={apiKey}
          onClose={() => setShowAdd(false)}
          onCreated={(id) => {
            selectDevice(id);
            setShowAdd(false);
            refresh();
            notify("Bowl added.");
          }}
        />
      )}
    </div>
  );
}

function Summary({
  icon,
  label,
  value,
}: {
  icon: ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div className="summary-row">
      <span className="metric-icon">{icon}</span>
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
      </div>
    </div>
  );
}
function Range({
  days,
  setDays,
}: {
  days: number;
  setDays: (days: number) => void;
}) {
  return (
    <div className="range-control" aria-label="Time range">
      {[1, 7].map((value) => (
        <button
          key={value}
          aria-pressed={days === value}
          className={days === value ? "selected" : ""}
          onClick={() => setDays(value)}
        >
          {value === 1 ? "24 hours" : "7 days"}
        </button>
      ))}
    </div>
  );
}
function DeviceSelect({
  statuses,
  selectedId,
  onChange,
}: {
  statuses: Status[];
  selectedId: string;
  onChange: (id: string) => void;
}) {
  return (
    <div className="select-wrap">
      <select
        aria-label="Selected bowl"
        value={selectedId}
        onChange={(event) => onChange(event.target.value)}
      >
        {statuses.map((status) => (
          <option key={status.deviceId} value={status.deviceId}>
            {status.name}
          </option>
        ))}
      </select>
      <ChevronDown size={16} />
    </div>
  );
}
function DeviceCard({
  device,
  selected,
  onClick,
}: {
  device: Status;
  selected: boolean;
  onClick: () => void;
}) {
  return (
    <button
      className={`device-card ${selected ? "selected-device" : ""}`}
      onClick={onClick}
      aria-pressed={selected}
    >
      <span className="device-icon">
        <Droplet size={25} strokeWidth={1.7} aria-hidden="true" />
      </span>
      <span className="device-copy">
        <strong>{device.name}</strong>
        <span>
          <span className={`status-dot ${isRecent(device) ? "" : "amber"}`} />
          {isRecent(device) ? "Recent upload" : ago(device.lastSeenAt)}
        </span>
      </span>
      <span className="selection-ring">{selected && <Check size={12} />}</span>
    </button>
  );
}

function ReadingsTable({
  readings,
  loading,
}: {
  readings: Reading[];
  loading: boolean;
}) {
  const [page, setPage] = useState(0);
  const reversed = [...readings].reverse();
  const exportCsv = () => {
    const csv = [
      "sequence,observedAt,weightGrams,rawReading",
      ...readings.map((reading) =>
        [
          reading.sequence,
          reading.observedAt,
          reading.weightGrams,
          reading.rawReading ?? "",
        ].join(","),
      ),
    ].join("\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = "bowl-readings.csv";
    link.click();
    URL.revokeObjectURL(url);
  };
  return (
    <div className="table-section">
      <div className="panel-heading">
        <h2>Recent readings</h2>
        <button
          className="button secondary"
          onClick={exportCsv}
          disabled={!readings.length}
        >
          <Download size={16} />
          Export CSV
        </button>
      </div>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Observed time</th>
              <th>Weight (g)</th>
              <th>Raw reading</th>
              <th>Sequence</th>
            </tr>
          </thead>
          <tbody>
            {reversed.slice(page * 50, (page + 1) * 50).map((reading) => (
              <tr key={reading.sequence}>
                <td>{dateTime(reading.observedAt)}</td>
                <td className="weight-cell">
                  {number.format(reading.weightGrams)}
                </td>
                <td>
                  {reading.rawReading === null
                    ? "—"
                    : number.format(reading.rawReading)}
                </td>
                <td>{reading.sequence}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {!readings.length && (
        <p className="table-message">
          {loading ? "Loading readings…" : "No readings to display."}
        </p>
      )}
      <div className="table-footer">
        <span>
          {readings.length
            ? `${page * 50 + 1}–${Math.min((page + 1) * 50, readings.length)} of ${readings.length}`
            : "0 readings"}
        </span>
        <div>
          <button
            className="text-button"
            disabled={page === 0}
            onClick={() => setPage(page - 1)}
          >
            Previous
          </button>
          <button
            className="text-button"
            disabled={(page + 1) * 50 >= readings.length}
            onClick={() => setPage(page + 1)}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}

function AccessSettings({
  apiKey,
  onSave,
}: {
  apiKey: string;
  onSave: (key: string) => void;
}) {
  const [key, setKey] = useState(apiKey);
  return (
    <section className="panel form-panel">
      <span className="form-icon">
        <Settings size={22} />
      </span>
      <h2>Device access</h2>
      <p>Your key allows this app to add bowls and save calibration values.</p>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          onSave(key.trim());
        }}
      >
        <label>
          Device API key
          <input
            type="password"
            autoComplete="off"
            value={key}
            onChange={(event) => setKey(event.target.value)}
            placeholder="Enter your device key"
          />
        </label>
        <p className="field-hint">
          The local setup uses <code>local-development-key</code>. The app keeps
          your key for this tab only.
        </p>
        <button className="button primary" type="submit">
          Save key
        </button>
      </form>
    </section>
  );
}
function CalibrationSettings({
  selected,
  statuses,
  onSelect,
  apiKey,
  onSaved,
}: {
  selected?: Status;
  statuses: Status[];
  onSelect: (id: string) => void;
  apiKey: string;
  onSaved: () => void;
}) {
  const [offset, setOffset] = useState(
    String(selected?.calibration.zero_offset ?? ""),
  );
  const [factor, setFactor] = useState(
    String(selected?.calibration.calibration_factor ?? ""),
  );
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!selected) return;
    setPending(true);
    setError("");
    try {
      await write<Calibration>(
        `/${selected.deviceId}/calibration`,
        { zero_offset: Number(offset), calibration_factor: Number(factor) },
        apiKey,
      );
      onSaved();
    } catch (error) {
      setError(errorText(error));
    } finally {
      setPending(false);
    }
  }
  return (
    <section className="panel form-panel">
      <span className="form-icon">
        <SlidersHorizontal size={22} />
      </span>
      <h2>Bowl calibration</h2>
      <p>
        Use the values from your device’s <code>calibration.json</code> file.
      </p>
      <DeviceSelect
        statuses={statuses}
        selectedId={selected?.deviceId ?? ""}
        onChange={onSelect}
      />
      <form onSubmit={submit}>
        <CalibrationFields
          offset={offset}
          factor={factor}
          setOffset={setOffset}
          setFactor={setFactor}
        />
        {error && (
          <p role="alert" className="form-error">
            {error}
          </p>
        )}
        <p className="field-hint">
          Calibration changes affect future readings. Stored weights remain
          unchanged.
        </p>
        <button
          className="button primary"
          type="submit"
          disabled={pending || !selected || !apiKey}
        >
          {pending ? "Saving…" : "Save calibration"}
        </button>
        {!apiKey && (
          <p className="field-hint">
            Save your device key to enable calibration changes.
          </p>
        )}
      </form>
    </section>
  );
}
function CalibrationFields({
  offset,
  factor,
  setOffset,
  setFactor,
}: {
  offset: string;
  factor: string;
  setOffset: (value: string) => void;
  setFactor: (value: string) => void;
}) {
  return (
    <div className="form-columns">
      <label>
        Zero offset
        <input
          type="number"
          step="any"
          min={-8388608}
          max={8388607}
          required
          value={offset}
          onChange={(event) => setOffset(event.target.value)}
          placeholder="e.g. -120000"
        />
      </label>
      <label>
        Calibration factor
        <input
          type="number"
          step="any"
          required
          value={factor}
          onChange={(event) => setFactor(event.target.value)}
          placeholder="e.g. -420.5"
        />
      </label>
    </div>
  );
}
function AddDevice({
  apiKey,
  onClose,
  onCreated,
}: {
  apiKey: string;
  onClose: () => void;
  onCreated: (id: string) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [name, setName] = useState("");
  const [offset, setOffset] = useState("");
  const [factor, setFactor] = useState("");
  const [key, setKey] = useState(apiKey);
  const [id] = useState(() => crypto.randomUUID());
  const [pending, setPending] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  async function submit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setError("");
    try {
      const device = await write<Device>(
        "",
        {
          deviceId: id,
          name: name.trim(),
          calibration: {
            zero_offset: Number(offset),
            calibration_factor: Number(factor),
          },
        },
        key.trim(),
      );
      onCreated(device.deviceId);
    } catch (error) {
      setError(errorText(error));
    } finally {
      setPending(false);
    }
  }
  return (
    <dialog ref={dialog} onCancel={onClose} className="add-dialog">
      <button
        className="icon-button dialog-close"
        aria-label="Close"
        onClick={onClose}
      >
        <X size={20} />
      </button>
      <span className="form-icon">
        <Droplets size={26} />
      </span>
      <h2>A new bowl. A familiar place.</h2>
      <p>Add the name and calibration values from your device.</p>
      <form onSubmit={submit}>
        <label>
          Bowl name
          <input
            autoFocus
            required
            maxLength={200}
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. Kitchen bowl"
          />
        </label>
        <CalibrationFields
          offset={offset}
          factor={factor}
          setOffset={setOffset}
          setFactor={setFactor}
        />
        <label>
          Device API key
          <input
            type="password"
            autoComplete="off"
            required
            value={key}
            onChange={(event) => setKey(event.target.value)}
            placeholder="Enter your device key"
          />
        </label>
        <p className="field-hint">
          A unique device GUID will identify this bowl.
        </p>
        {error && (
          <p role="alert" className="form-error">
            {error}
          </p>
        )}
        <div className="dialog-actions">
          <button className="button secondary" type="button" onClick={onClose}>
            Cancel
          </button>
          <button className="button primary" type="submit" disabled={pending}>
            {pending ? "Adding…" : "Add bowl"}
          </button>
        </div>
      </form>
    </dialog>
  );
}
