export interface TeeTime {
  id: number;
  course: string;
  startsAt: string;
  capacity: number;
  bookedPlayers: number;
  availableSpots: number;
}

export interface Booking {
  id: string;
  teeTimeId: number;
  playerName: string;
  players: number;
  status: "Confirmed" | "Cancelled";
  createdAtUtc: string;
}

export interface BookAttempt {
  status: number;
  replayed: boolean;
  booking?: Booking;
  problem?: { title: string; availableSpots?: number };
}

export async function getTeeTimes(date: string): Promise<TeeTime[]> {
  const res = await fetch(`/api/tee-times?date=${date}`);
  if (!res.ok) throw new Error(`Loading tee times failed: ${res.status}`);
  return res.json();
}

export async function getBookings(teeTimeId: number): Promise<Booking[]> {
  const res = await fetch(`/api/tee-times/${teeTimeId}/bookings`);
  if (!res.ok) throw new Error(`Loading bookings failed: ${res.status}`);
  return res.json();
}

/** The key identifies the booking attempt: reuse it on every retry of the same attempt. */
export async function book(teeTimeId: number, playerName: string, players: number, idempotencyKey: string): Promise<BookAttempt> {
  const res = await fetch(`/api/tee-times/${teeTimeId}/bookings`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ playerName, players }),
  });
  const body = await res.json().catch(() => undefined);
  return res.ok
    ? { status: res.status, replayed: res.headers.get("Idempotent-Replayed") === "true", booking: body }
    : { status: res.status, replayed: false, problem: body };
}

export async function cancel(bookingId: string): Promise<void> {
  const res = await fetch(`/api/bookings/${bookingId}/cancel`, { method: "POST" });
  if (!res.ok) throw new Error(`Cancel failed: ${res.status}`);
}

export async function resetDemo(): Promise<void> {
  await fetch("/api/demo/reset", { method: "POST" });
}

export interface AppVersion {
  version: string;
  commit: string | null;
  builtAtUtc: string | null;
  environment: string;
}

export async function getVersion(): Promise<AppVersion> {
  const res = await fetch("/api/version");
  if (!res.ok) throw new Error(`Loading version failed: ${res.status}`);
  return res.json();
}
