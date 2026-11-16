/** What the UI shows of a ticket without asking the server: a JWT's header and payload are only base64url JSON. */
export interface DecodedTicket {
  kid: string;
  bookingId: string;
  teeTimeId: number;
  players: number;
  startsAt: string;
}

function fromBase64Url(part: string): string {
  const base64 = part.replace(/-/g, "+").replace(/_/g, "/");
  return new TextDecoder().decode(Uint8Array.from(atob(base64), (c) => c.charCodeAt(0)));
}

function toBase64Url(text: string): string {
  const bytes = new TextEncoder().encode(text);
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** Reads a ticket; undefined for anything that is not one. Reading proves nothing: only the signature does. */
export function decodeTicket(token: string): DecodedTicket | undefined {
  const parts = token.trim().split(".");
  if (parts.length !== 3) return undefined;
  try {
    const header = JSON.parse(fromBase64Url(parts[0]));
    const payload = JSON.parse(fromBase64Url(parts[1]));
    return { kid: header.kid, bookingId: payload.sub, teeTimeId: payload.teeTime, players: payload.players, startsAt: payload.startsAt };
  } catch {
    return undefined;
  }
}

/**
 * The same ticket claiming a different number of players, its signature untouched: what a forger would do.
 * The server must answer BadSignature.
 */
export function tamper(token: string): string {
  const [header, payload, signature] = token.trim().split(".");
  const claims = JSON.parse(fromBase64Url(payload));
  claims.players = (claims.players % 4) + 1;
  return [header, toBase64Url(JSON.stringify(claims)), signature].join(".");
}
