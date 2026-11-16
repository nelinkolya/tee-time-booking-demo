import { describe, expect, it } from "vitest";
import { decodeTicket, tamper } from "./ticket";

const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const header = b64({ alg: "ES256", typ: "JWT", kid: "k1" });
const payload = b64({ sub: "3f2a9c1e-0000-0000-0000-000000000001", teeTime: 7, players: 2, startsAt: "2026-11-03T07:10:00", iat: 1 });
const token = `${header}.${payload}.c2lnbmF0dXJl`;

describe("ticket", () => {
  it("reads the key version and the booking from a ticket", () => {
    expect(decodeTicket(token)).toEqual({
      kid: "k1",
      bookingId: "3f2a9c1e-0000-0000-0000-000000000001",
      teeTimeId: 7,
      players: 2,
      startsAt: "2026-11-03T07:10:00",
    });
  });

  it("reads nothing from what is not a ticket", () => {
    expect(decodeTicket("")).toBeUndefined();
    expect(decodeTicket("not.a.ticket")).toBeUndefined();
  });

  it("forges a different player count but keeps the header and the signature", () => {
    const [h, p, s] = tamper(token).split(".");

    expect(h).toBe(header);
    expect(s).toBe("c2lnbmF0dXJl");
    expect(p).not.toBe(payload);
    expect(decodeTicket(`${h}.${p}.${s}`)?.players).toBe(3);
  });
});
