export interface Calibration {
  zero_offset: number;
  calibration_factor: number;
}
export interface Reading {
  sequence: number;
  observedAt: string;
  weightGrams: number;
  rawReading: number | null;
}
export interface Device {
  deviceId: string;
  name: string;
  calibration: Calibration;
}
export interface Status extends Device {
  lastSeenAt: string | null;
  latestReading: Reading | null;
}
interface ReadingPage {
  readings: Reading[];
  nextAfterSequence: number | null;
}

export async function request<T>(
  path: string,
  options?: RequestInit,
): Promise<T> {
  const response = await fetch(`/api/v1/devices${path}`, options);
  if (!response.ok) {
    if (response.status === 401)
      throw new Error("Enter a valid device API key in Settings.");
    let detail = `The request failed (HTTP ${response.status}).`;
    try {
      const problem = await response.json();
      detail = problem.detail ?? detail;
    } catch {
      /* Keep the HTTP error. */
    }
    throw new Error(detail);
  }
  return response.json() as Promise<T>;
}

export async function getReadings(
  deviceId: string,
  days: number,
  signal: AbortSignal,
): Promise<Reading[]> {
  const from = new Date(Date.now() - days * 86_400_000).toISOString();
  const to = new Date().toISOString();
  let cursor: number | null = -1;
  const readings: Reading[] = [];
  do {
    const page: ReadingPage = await request(
      `/${deviceId}/readings?limit=1000&afterSequence=${cursor}&from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      { signal },
    );
    readings.push(...page.readings);
    cursor = page.nextAfterSequence;
  } while (cursor !== null);
  return readings.sort(
    (a, b) =>
      Date.parse(a.observedAt) - Date.parse(b.observedAt) ||
      a.sequence - b.sequence,
  );
}

export function write<T>(path: string, body: unknown, key: string): Promise<T> {
  return request(path, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Device-Key": key },
    body: JSON.stringify(body),
  });
}
