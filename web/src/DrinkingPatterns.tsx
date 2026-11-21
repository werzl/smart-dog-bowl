import { useEffect, useState } from "react";
import { Clock3, Droplet, Moon, Sun } from "lucide-react";
import type { ReactNode } from "react";
import { getDrinkingSummary } from "./api";
import type { DrinkingSummary } from "./api";

const number = new Intl.NumberFormat("en-GB", { maximumFractionDigits: 1 });
function hourRange(hour: number) {
  return `${String(hour).padStart(2, "0")}:00–${String((hour + 1) % 24).padStart(2, "0")}:00`;
}
function duration(seconds: number | null) {
  if (seconds === null) return "Unavailable";
  if (seconds < 60) return `${number.format(seconds)} s`;
  const rounded = Math.round(seconds);
  const minutes = Math.floor(rounded / 60),
    remainder = rounded % 60;
  return remainder ? `${minutes} min ${remainder} s` : `${minutes} min`;
}

export default function DrinkingPatterns({
  deviceId,
  days,
  revision,
  onRetry,
}: {
  deviceId: string;
  days: number;
  revision: number;
  onRetry: () => void;
}) {
  const [summary, setSummary] = useState<DrinkingSummary | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    if (!deviceId) {
      setLoading(false);
      return;
    }
    const controller = new AbortController();
    setLoading(true);
    getDrinkingSummary(deviceId, days, controller.signal)
      .then((summary) => {
        setSummary(summary);
        setError("");
      })
      .catch((error) => {
        if (!controller.signal.aborted)
          setError(
            error instanceof Error
              ? error.message
              : "The drinking summary is unavailable.",
          );
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [deviceId, days, revision]);
  const hasData = !!summary && summary.observedCoverageSeconds > 0;
  const most = summary?.mostConsumedHours ?? [],
    least = summary?.leastConsumedHours ?? [];
  const hourHint = (hours: number[]) =>
    hours.length > 1
      ? `One of ${hours.length} equal hours`
      : hours.length
        ? "Across observed hours"
        : hasData
          ? "No clear pattern yet"
          : "No readings in this period";
  const initial = loading && !summary;

  return (
    <section
      className="drinking-patterns"
      aria-label="Drinking patterns"
      aria-busy={loading}
    >
      <div className="patterns-heading">
        <div>
          <h2>Drinking patterns</h2>
          <p>Estimated from stable changes in bowl weight.</p>
        </div>
        <span className="period-badge">
          {days === 1 ? "Last 24 hours" : "Last 7 days"}
        </span>
      </div>
      {error ? (
        <div className="patterns-error" role="alert">
          <span>{error}</span>
          <button className="text-button" onClick={onRetry}>
            Try again
          </button>
        </div>
      ) : (
        <div className="pattern-grid">
          <PatternMetric
            icon={<Droplet size={19} />}
            label="Estimated water consumed"
            value={
              initial || !hasData
                ? "—"
                : `${number.format(summary!.estimatedConsumedMilliliters)} mL`
            }
            hint={
              initial
                ? "Loading period…"
                : hasData
                  ? "From candidate drinking events"
                  : "No readings in this period"
            }
          />
          <PatternMetric
            icon={<Sun size={19} />}
            label="Most water consumed"
            value={initial ? "—" : most.length ? hourRange(most[0]) : "—"}
            hint={initial ? "Loading period…" : hourHint(most)}
            title={most.map(hourRange).join(", ")}
          />
          <PatternMetric
            icon={<Moon size={19} />}
            label="Least water consumed"
            value={initial ? "—" : least.length ? hourRange(least[0]) : "—"}
            hint={initial ? "Loading period…" : hourHint(least)}
            title={least.map(hourRange).join(", ")}
          />
          <PatternMetric
            icon={<Clock3 size={19} />}
            label="Average drink duration"
            value={
              initial
                ? "—"
                : duration(summary?.averageDrinkDurationSeconds ?? null)
            }
            hint={
              initial
                ? "Loading period…"
                : summary?.averageDrinkDurationSeconds != null
                  ? "From measurable candidate events"
                  : summary?.events.length
                    ? "Readings must be at most two seconds apart."
                    : "No candidate drinks in this period"
            }
          />
        </div>
      )}
      {summary && !error && (
        <div className="patterns-notes">
          <p>Water loss can include spills. These values are estimates.</p>
          <p>
            Times use {summary.timeZone}. Unobserved hours do not count as quiet
            hours.
            {summary.observedCoverageSeconds < days * 86_400 * 0.95 &&
              ` Readings cover ${number.format(summary.observedCoverageSeconds / 3600)} h of this period.`}
          </p>
        </div>
      )}
      {!!summary?.events.length && !error && (
        <details className="drinking-events">
          <summary>View drinking events</summary>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Event time</th>
                  <th>Estimated amount</th>
                  <th>Duration</th>
                </tr>
              </thead>
              <tbody>
                {[...summary.events].reverse().map((event) => (
                  <tr key={event.startedAt}>
                    <td>
                      {new Date(event.startedAt).toLocaleString("en-GB", {
                        day: "numeric",
                        month: "short",
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </td>
                    <td>{number.format(event.estimatedMilliliters)} mL</td>
                    <td>{duration(event.durationSeconds)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}
    </section>
  );
}
function PatternMetric({
  icon,
  label,
  value,
  hint,
  title,
}: {
  icon: ReactNode;
  label: string;
  value: string;
  hint: string;
  title?: string;
}) {
  return (
    <div className="pattern-metric">
      <span className="pattern-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="pattern-label">{label}</span>
      <strong title={title}>{value}</strong>
      <span className="pattern-hint">{hint}</span>
    </div>
  );
}
