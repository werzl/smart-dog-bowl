import { useEffect, useId, useRef, useState } from "react";
import type { PointerEvent } from "react";
import { Waves } from "lucide-react";
import type { Reading } from "./api";

const weightFormat = new Intl.NumberFormat("en-GB", {
  maximumFractionDigits: 1,
});
const timeLabel = (value: string) =>
  new Date(value).toLocaleString("en-GB", {
    day: "numeric",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });

export default function WaterChart({
  readings,
  loading,
  days,
  error,
}: {
  readings: Reading[];
  loading: boolean;
  days: number;
  error: string;
}) {
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
  const container = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(800);
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) =>
      setWidth(Math.max(200, entry.contentRect.width)),
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [readings.length]);
  if (!readings.length)
    return (
      <div className="chart-empty">
        <Waves size={30} />
        <p>
          {loading
            ? "Loading readings…"
            : error
              ? "Readings are unavailable."
              : "No readings in this time range."}
        </p>
      </div>
    );

  const height = width < 480 ? 265 : 290;
  const left = width < 480 ? 48 : 58,
    right = 14,
    top = 18,
    bottom = 37;
  const end = Date.now(),
    start = end - days * 86_400_000;
  const range = readings.reduce(
    (range, reading) => ({
      min: Math.min(range.min, reading.weightGrams),
      max: Math.max(range.max, reading.weightGrams),
    }),
    { min: 0, max: 100 },
  );
  const step = Math.pow(
    10,
    Math.floor(Math.log10((range.max - range.min) / 4 || 1)),
  );
  const interval =
    [1, 2, 2.5, 5, 10]
      .map((value) => value * step)
      .find((value) => value >= (range.max - range.min) / 4) ?? step * 10;
  const min = Math.floor(range.min / interval) * interval;
  const max = Math.ceil(range.max / interval) * interval;
  const yTicks = Array.from(
    { length: Math.round((max - min) / interval) + 1 },
    (_, index) => min + interval * index,
  );
  const x = (time: number) =>
    left + ((time - start) / (end - start)) * (width - left - right);
  const y = (weight: number) =>
    top + ((max - weight) / (max - min || 1)) * (height - top - bottom);
  const points = readings
    .map(
      (reading) =>
        `${x(Date.parse(reading.observedAt))},${y(reading.weightGrams)}`,
    )
    .join(" ");
  const selected =
    readings[Math.min(activeIndex ?? readings.length - 1, readings.length - 1)];
  const selectedX = x(Date.parse(selected.observedAt)),
    selectedY = y(selected.weightGrams);
  const selectedWeight = `${weightFormat.format(selected.weightGrams)} g`;
  const tooltipLeft = Math.max(0, Math.min(width - 166, selectedX - 83));
  const tooltipTop = Math.max(4, Math.min(height - 80, selectedY - 78));

  function inspect(event: PointerEvent<SVGSVGElement>) {
    const bounds = event.currentTarget.getBoundingClientRect();
    const time =
      start +
      ((((event.clientX - bounds.left) / bounds.width) * width - left) /
        (width - left - right)) *
        (end - start);
    let low = 0,
      high = readings.length - 1;
    while (low < high) {
      const middle = Math.floor((low + high) / 2);
      if (Date.parse(readings[middle].observedAt) < time) low = middle + 1;
      else high = middle;
    }
    if (
      low > 0 &&
      Math.abs(Date.parse(readings[low - 1].observedAt) - time) <
        Math.abs(Date.parse(readings[low].observedAt) - time)
    )
      low--;
    setActiveIndex(low);
  }

  return (
    <div className="chart-container readable-chart" ref={container}>
      <div className="chart-toolbar">
        <div className="chart-legend">
          <span />
          <span>
            Weight <span className="chart-unit">(g)</span>
          </span>
        </div>
        <div className="chart-readout" id={`${id}-value`}>
          <strong>{selectedWeight}</strong>
          <span>
            {activeIndex === null ? "Latest sample · " : ""}
            {timeLabel(selected.observedAt)}
          </span>
        </div>
      </div>
      <div className="chart-plot">
        <svg
          className="water-chart"
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          tabIndex={0}
          aria-label={`Bowl weight over the past ${days === 1 ? "24 hours" : "7 days"}`}
          aria-describedby={`${id}-help ${id}-value`}
          onPointerMove={inspect}
          onPointerDown={inspect}
          onPointerLeave={(event) => {
            if (event.pointerType !== "touch") setActiveIndex(null);
          }}
          onFocus={() =>
            setActiveIndex((index) => index ?? readings.length - 1)
          }
          onBlur={() => setActiveIndex(null)}
          onKeyDown={(event) => {
            if (
              ["ArrowLeft", "ArrowRight", "Home", "End", "Escape"].includes(
                event.key,
              )
            ) {
              event.preventDefault();
              if (event.key === "Escape") setActiveIndex(null);
              else if (event.key === "Home") setActiveIndex(0);
              else if (event.key === "End") setActiveIndex(readings.length - 1);
              else
                setActiveIndex((index) =>
                  Math.max(
                    0,
                    Math.min(
                      readings.length - 1,
                      (index ?? readings.length - 1) +
                        (event.key === "ArrowLeft" ? -1 : 1),
                    ),
                  ),
                );
            }
          }}
        >
          <defs>
            <linearGradient id={`${id}-fill`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#80c7ff" stopOpacity="0.22" />
              <stop offset="100%" stopColor="#80c7ff" stopOpacity="0.015" />
            </linearGradient>
          </defs>
          {yTicks.map((weight) => (
            <g key={weight}>
              <line
                x1={left}
                x2={width - right}
                y1={y(weight)}
                y2={y(weight)}
                className={`chart-grid ${weight === 0 ? "chart-zero" : ""}`}
              />
              <text x={left - 11} y={y(weight) + 5} textAnchor="end">
                {weightFormat.format(weight)}
              </text>
            </g>
          ))}
          <polygon
            points={`${x(Date.parse(readings[0].observedAt))},${height - bottom} ${points} ${x(Date.parse(readings.at(-1)!.observedAt))},${height - bottom}`}
            fill={`url(#${id}-fill)`}
          />
          <polyline
            points={points}
            fill="none"
            stroke="#8dceff"
            strokeWidth="3"
            strokeLinejoin="round"
            strokeLinecap="round"
            vectorEffect="non-scaling-stroke"
          />
          <circle
            cx={x(Date.parse(readings.at(-1)!.observedAt))}
            cy={y(readings.at(-1)!.weightGrams)}
            r="4.5"
            fill="#d1edff"
            stroke="#1b2c40"
            strokeWidth="2"
          />
          {activeIndex !== null && (
            <g>
              <line
                x1={selectedX}
                x2={selectedX}
                y1={top}
                y2={height - bottom}
                stroke="#a3b7ce"
                strokeDasharray="4 4"
              />
              <circle
                cx={selectedX}
                cy={selectedY}
                r="6"
                fill="#d5f0ff"
                stroke="#1b2c40"
                strokeWidth="3"
              />
            </g>
          )}
          {(width < 480 ? [0, 0.5, 1] : [0, 0.25, 0.5, 0.75, 1]).map(
            (fraction) => (
              <text
                key={fraction}
                x={x(start + (end - start) * fraction)}
                y={height - 7}
                textAnchor={
                  fraction === 0 ? "start" : fraction === 1 ? "end" : "middle"
                }
              >
                {new Date(start + (end - start) * fraction).toLocaleString(
                  "en-GB",
                  days === 1
                    ? { hour: "2-digit", minute: "2-digit" }
                    : { day: "numeric", month: "short" },
                )}
              </text>
            ),
          )}
        </svg>
        {activeIndex !== null && (
          <div
            className="chart-tooltip"
            aria-hidden="true"
            style={{ left: tooltipLeft, top: tooltipTop }}
          >
            <strong>{selectedWeight}</strong>
            <span>{timeLabel(selected.observedAt)}</span>
          </div>
        )}
      </div>
      <div className="chart-caption">
        <span>Hover or tap for exact values.</span>
        <span>{weightFormat.format(readings.length)} readings</span>
      </div>
      <span className="visually-hidden" id={`${id}-help`}>
        Use the Left and Right arrow keys to inspect readings. Home selects the
        first reading. End selects the last reading.
      </span>
    </div>
  );
}
