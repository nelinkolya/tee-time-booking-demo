import { describe, expect, it, vi } from "vitest";
import { time, today } from "./format";

describe("today", () => {
  it("is the local date, not the UTC one, just after midnight", () => {
    // 00:30 on 3 November in Kyiv is still 2 November in UTC.
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 3, 0, 30));

    expect(today()).toBe("2026-11-03");
  });

  it("pads month and day to two digits", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 0, 5, 12, 0));

    expect(today()).toBe("2026-01-05");
  });
});

describe("time", () => {
  it("takes hh:mm from the API's course-local timestamp", () => {
    expect(time("2026-11-03T07:10:00")).toBe("07:10");
  });
});
