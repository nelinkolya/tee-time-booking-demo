import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getTeeTimes, type TeeTime } from "../api";
import { useTeeTimes } from "./useTeeTimes";

vi.mock("../api");

const slot = (id: number, startsAt: string): TeeTime => ({ id, course: "Riverside Links", startsAt, capacity: 4, bookedPlayers: 0, availableSpots: 4 });

describe("useTeeTimes", () => {
  beforeEach(() => {
    vi.resetAllMocks();
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
      return Promise.resolve([slot(2, "2026-11-05T07:10:00")]);
    });
    const { result, rerender } = renderHook(({ date }) => useTeeTimes(date, undefined), { initialProps: { date: "2026-11-04" } });

    rerender({ date: "2026-11-05" });
    await waitFor(() => expect(result.current.teeTimes).toEqual([slot(2, "2026-11-05T07:10:00")]));
    await act(async () => answerSlowly([slot(1, "2026-11-04T09:00:00")]));

    expect(signals.get("2026-11-04")?.aborted).toBe(true);
    expect(result.current.teeTimes).toEqual([slot(2, "2026-11-05T07:10:00")]);
    expect(result.current.error).toBeUndefined();
  });

  it("reads the tee times again after a change succeeds", async () => {
    vi.mocked(getTeeTimes).mockResolvedValue([]);
    const { result } = renderHook(() => useTeeTimes("2026-11-05", undefined));
    await waitFor(() => expect(getTeeTimes).toHaveBeenCalledTimes(1));

    const booked = { ...slot(1, "2026-11-05T07:10:00"), bookedPlayers: 2, availableSpots: 2 };
    vi.mocked(getTeeTimes).mockResolvedValue([booked]);
    await act(() => result.current.change(() => Promise.resolve()));

    expect(result.current.teeTimes).toEqual([booked]);
  });

  it("shows a failed change and does not read again", async () => {
    vi.mocked(getTeeTimes).mockResolvedValue([]);
    const { result } = renderHook(() => useTeeTimes("2026-11-05", undefined));
    await waitFor(() => expect(getTeeTimes).toHaveBeenCalledTimes(1));

    await act(() => result.current.change(() => Promise.reject(new Error("Cancel failed: 500"))));

    expect(result.current.error).toBe("Error: Cancel failed: 500");
    expect(getTeeTimes).toHaveBeenCalledTimes(1);
  });
});
