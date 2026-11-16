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

export interface BookingRequest {
  playerName: string;
  players: number;
}

export interface BookAttempt {
  status: number;
  replayed: boolean;
  booking?: Booking;
  problem?: { title: string; availableSpots?: number };
}

export async function getTeeTimes(date: string, signal?: AbortSignal): Promise<TeeTime[]> {
  const res = await fetch(`/api/tee-times?date=${date}`, { signal });
  if (!res.ok) throw new Error(`Loading tee times failed: ${res.status}`);
  return res.json();
}

export async function getBookings(teeTimeId: number, signal?: AbortSignal): Promise<Booking[]> {
  const res = await fetch(`/api/tee-times/${teeTimeId}/bookings`, { signal });
  if (!res.ok) throw new Error(`Loading bookings failed: ${res.status}`);
  return res.json();
}

/** The key identifies the booking attempt: reuse it on every retry of the same attempt. */
export async function book(teeTimeId: number, playerName: string, players: number, idempotencyKey: string): Promise<BookAttempt> {
  const res = await fetch(`/api/tee-times/${teeTimeId}/bookings`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ playerName, players } satisfies BookingRequest),
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

export interface Ticket {
  token: string;
}

/** A ticket's statement, checked by the server, with the booking's status now. */
export interface TicketCheck {
  bookingId: string;
  teeTimeId: number;
  players: number;
  startsAt: string;
  issuedAtUtc: string;
  status: "Confirmed" | "Cancelled";
}

export type TicketResult = { ok: true; token: string } | { ok: false; status: number; title: string };

/** reason: Malformed, UnknownKey, BadSignature or BookingNotFound. */
export type VerifyResult = { ok: true; check: TicketCheck } | { ok: false; status: number; title: string; reason?: string };

export async function getTicket(bookingId: string): Promise<TicketResult> {
  const res = await fetch(`/api/bookings/${bookingId}/ticket`);
  const body = await res.json().catch(() => undefined);
  return res.ok
    ? { ok: true, token: (body as Ticket).token }
    : { ok: false, status: res.status, title: body?.title ?? `Getting the ticket failed: ${res.status}` };
}

export async function verifyTicket(token: string): Promise<VerifyResult> {
  const res = await fetch("/api/tickets/verify", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token }),
  });
  const body = await res.json().catch(() => undefined);
  return res.ok
    ? { ok: true, check: body as TicketCheck }
    : { ok: false, status: res.status, title: body?.title ?? `Verifying failed: ${res.status}`, reason: body?.reason };
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
