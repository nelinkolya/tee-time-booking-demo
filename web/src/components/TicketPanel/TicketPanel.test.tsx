import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { verifyTicket } from "../../api";
import { decodeTicket } from "../../ticket";
import { TicketPanel } from "./TicketPanel";

vi.mock("../../api");

const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const token = `${b64({ alg: "ES256", kid: "k1" })}.${b64({ sub: "b1", teeTime: 7, players: 2, startsAt: "2026-11-03T07:10:00" })}.c2ln`;
const check = { bookingId: "b1", teeTimeId: 7, players: 2, startsAt: "2026-11-03T07:10:00", issuedAtUtc: "2026-11-03T06:00:00Z" };

describe("TicketPanel", () => {
  beforeEach(() => vi.mocked(verifyTicket).mockReset());

  it("verifies the ticket shown and says it is valid", async () => {
    vi.mocked(verifyTicket).mockResolvedValue({ ok: true, check: { ...check, status: "Confirmed" } });
    const user = userEvent.setup();
    render(<TicketPanel token={token} onLog={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Verify" }));

    expect(verifyTicket).toHaveBeenCalledWith(token);
    expect(await screen.findByRole("status")).toHaveTextContent("✓ Valid — 2 players at 07:10");
  });

  it("sends the forged ticket after a byte is changed, and explains the rejection", async () => {
    vi.mocked(verifyTicket).mockResolvedValue({ ok: false, status: 422, title: "Not a valid ticket.", reason: "BadSignature" });
    const user = userEvent.setup();
    render(<TicketPanel token={token} onLog={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Change a byte" }));
    await user.click(screen.getByRole("button", { name: "Verify" }));

    const sent = vi.mocked(verifyTicket).mock.calls[0][0];
    expect(sent).not.toBe(token);
    expect(decodeTicket(sent)?.players).toBe(3);
    expect(await screen.findByRole("status")).toHaveTextContent("changed after it was signed");
  });

  it("drops an answer for text that changed while it was on its way", async () => {
    let answer!: (r: Awaited<ReturnType<typeof verifyTicket>>) => void;
    vi.mocked(verifyTicket).mockReturnValue(new Promise((resolve) => (answer = resolve)));
    const user = userEvent.setup();
    render(<TicketPanel token={token} onLog={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Verify" }));
    await user.click(screen.getByRole("button", { name: "Change a byte" }));
    answer({ ok: true, check: { ...check, status: "Confirmed" } });
    await new Promise((r) => setTimeout(r, 0));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("logs a copy the browser refused instead of seeming to do nothing", async () => {
    const onLog = vi.fn();
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, "writeText").mockRejectedValue(new Error("NotAllowedError"));
    render(<TicketPanel token={token} onLog={onLog} />);

    await user.click(screen.getByRole("button", { name: "Copy" }));

    expect(onLog).toHaveBeenCalledWith({ kind: "error", text: "Copy failed: Error: NotAllowedError" });
    expect(screen.getByRole("button", { name: "Copy" })).toBeInTheDocument();
  });

  it("says a validly signed ticket's booking was cancelled", async () => {
    vi.mocked(verifyTicket).mockResolvedValue({ ok: true, check: { ...check, status: "Cancelled" } });
    const user = userEvent.setup();
    render(<TicketPanel token={token} onLog={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Verify" }));

    expect(await screen.findByRole("status")).toHaveTextContent("cancelled");
  });
});
