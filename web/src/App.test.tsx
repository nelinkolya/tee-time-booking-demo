import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { book, cancel, getBookings, getTeeTimes, getVersion, type Booking, type TeeTime } from "./api";
import { App } from "./App";

vi.mock("./api");

const slot = (id: number, startsAt: string): TeeTime => ({ id, course: "Riverside Links", startsAt, capacity: 4, bookedPlayers: 0, availableSpots: 4 });

describe("App", () => {
  beforeEach(() => {
    vi.mocked(getVersion).mockReturnValue(new Promise(() => {}));
  });

  it("shows a failed cancel instead of losing it", async () => {
    const booking: Booking = { id: "b1", teeTimeId: 1, playerName: "Alex", players: 2, status: "Confirmed", createdAtUtc: "2026-11-03T06:00:00Z" };
    vi.mocked(getTeeTimes).mockResolvedValue([slot(1, "2026-11-03T07:10:00")]);
    vi.mocked(getBookings).mockResolvedValue([booking]);
    vi.mocked(cancel).mockRejectedValue(new Error("Cancel failed: 500"));
    const user = userEvent.setup();
    render(<App />);

    await user.click(await screen.findByRole("button", { name: /07:10/ }));
    await user.click(await screen.findByRole("button", { name: "Cancel" }));

    expect(await screen.findByText("Error: Cancel failed: 500")).toBeInTheDocument();
  });

  it("books a different tee time with a new idempotency key", async () => {
    vi.mocked(getTeeTimes).mockResolvedValue([slot(1, "2026-11-03T07:10:00"), slot(2, "2026-11-03T07:20:00")]);
    vi.mocked(getBookings).mockResolvedValue([]);
    vi.mocked(book).mockResolvedValue({ status: 409, replayed: false, problem: { title: "Not enough free spots." } });
    const user = userEvent.setup();
    render(<App />);

    await user.click(await screen.findByRole("button", { name: /07:10/ }));
    await user.click(await screen.findByRole("button", { name: "Book" }));
    await user.click(screen.getByRole("button", { name: /07:20/ }));
    await user.click(await screen.findByRole("button", { name: "Book" }));

    const [[firstSlot, , , firstKey], [secondSlot, , , secondKey]] = vi.mocked(book).mock.calls;
    expect([firstSlot, secondSlot]).toEqual([1, 2]);
    expect(secondKey).not.toBe(firstKey);
  });
});
