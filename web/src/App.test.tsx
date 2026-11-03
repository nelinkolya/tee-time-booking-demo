import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { cancel, getBookings, getTeeTimes, getVersion, type Booking, type TeeTime } from "./api";
import { App } from "./App";

vi.mock("./api");

const slot = (id: number, startsAt: string): TeeTime => ({ id, course: "Riverside Links", startsAt, capacity: 4, bookedPlayers: 0, availableSpots: 4 });

describe("App", () => {
  beforeEach(() => {
    vi.mocked(getVersion).mockReturnValue(new Promise(() => {}));
  });

  it("drops a stale response when the date changes while it is still loading", async () => {
    // The 4th answers slowly — after the 5th — unless its request is aborted first.
    let answerSlowly!: (teeTimes: TeeTime[]) => void;
    const signals = new Map<string, AbortSignal | undefined>();
    vi.mocked(getTeeTimes).mockImplementation((date, signal) => {
      signals.set(date, signal);
      if (date === "2026-11-04") {
        return new Promise((resolve, reject) => {
          answerSlowly = resolve;
          signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
        });
      }
      if (date === "2026-11-05") return Promise.resolve([slot(2, "2026-11-05T07:10:00")]);
      return Promise.resolve([]);
    });
    const { container } = render(<App />);
    const dateInput = container.querySelector<HTMLInputElement>('input[type="date"]')!;

    fireEvent.change(dateInput, { target: { value: "2026-11-04" } });
    fireEvent.change(dateInput, { target: { value: "2026-11-05" } });
    expect(await screen.findByText("07:10")).toBeInTheDocument();

    await act(async () => answerSlowly([slot(1, "2026-11-04T09:00:00")]));

    expect(signals.get("2026-11-04")?.aborted).toBe(true);
    expect(screen.queryByText("09:00")).not.toBeInTheDocument();
    expect(screen.getByText("07:10")).toBeInTheDocument();
    expect(container.querySelector(".error")).toBeNull();
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
});
