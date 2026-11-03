import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { book, type BookAttempt, type TeeTime } from "../api";
import { BookingForm } from "./BookingForm";

vi.mock("../api");

const teeTime: TeeTime = { id: 7, course: "Riverside Links", startsAt: "2026-11-03T07:10:00", capacity: 4, bookedPlayers: 0, availableSpots: 4 };
const full: BookAttempt = { status: 409, replayed: false, problem: { title: "Not enough free spots.", availableSpots: 0 } };
const created: BookAttempt = { status: 201, replayed: false };

/** The Idempotency-Key sent with each call to book(). */
const sentKeys = () => vi.mocked(book).mock.calls.map((call) => call[3]);

describe("BookingForm idempotency key", () => {
  beforeEach(() => {
    vi.mocked(book).mockReset();
  });

  it("sends the same key when the attempt is repeated before it succeeds", async () => {
    vi.mocked(book).mockResolvedValue(full);
    const user = userEvent.setup();
    render(<BookingForm teeTime={teeTime} onResult={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Book" }));
    await user.click(screen.getByRole("button", { name: "Book" }));

    const [first, second] = sentKeys();
    expect(second).toBe(first);
  });

  it("starts a new attempt with a new key once a booking succeeded", async () => {
    vi.mocked(book).mockResolvedValue(created);
    const user = userEvent.setup();
    render(<BookingForm teeTime={teeTime} onResult={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Book" }));
    await user.click(screen.getByRole("button", { name: "Book" }));

    const [first, second] = sentKeys();
    expect(second).not.toBe(first);
  });

  it("uses a new key when the request changes", async () => {
    vi.mocked(book).mockResolvedValue(full);
    const user = userEvent.setup();
    render(<BookingForm teeTime={teeTime} onResult={() => {}} />);

    await user.click(screen.getByRole("button", { name: "Book" }));
    await user.selectOptions(screen.getByLabelText("Players"), "3");
    await user.click(screen.getByRole("button", { name: "Book" }));

    const [first, second] = sentKeys();
    expect(second).not.toBe(first);
    expect(vi.mocked(book).mock.calls[1]).toEqual([7, "Alex", 3, second]);
  });

  it("reports every server answer to the parent", async () => {
    vi.mocked(book).mockResolvedValue(full);
    const onResult = vi.fn();
    const user = userEvent.setup();
    render(<BookingForm teeTime={teeTime} onResult={onResult} />);

    await user.click(screen.getByRole("button", { name: "Book" }));

    expect(onResult).toHaveBeenCalledWith(full);
  });

  it("reports a request that got no answer and keeps the key for the retry", async () => {
    vi.mocked(book).mockRejectedValueOnce(new TypeError("Failed to fetch")).mockResolvedValueOnce(created);
    const onResult = vi.fn();
    const user = userEvent.setup();
    render(<BookingForm teeTime={teeTime} onResult={onResult} />);

    await user.click(screen.getByRole("button", { name: "Book" }));
    await user.click(screen.getByRole("button", { name: "Book" }));

    expect(onResult).toHaveBeenNthCalledWith(1, { status: 0, replayed: false, problem: { title: "TypeError: Failed to fetch" } });
    const [first, second] = sentKeys();
    expect(second).toBe(first);
  });
});
