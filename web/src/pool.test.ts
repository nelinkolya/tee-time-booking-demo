import { describe, expect, it } from "vitest";
import { inPool } from "./pool";

/** A call that stays in flight until the test resolves it. */
function deferred() {
  let resolve!: (value: number) => void;
  const promise = new Promise<number>((r) => (resolve = r));
  return { promise, resolve };
}

describe("inPool", () => {
  it("never has more than the limit in flight", async () => {
    let inFlight = 0;
    let peak = 0;
    const calls = Array.from({ length: 20 }, (_, i) => async () => {
      peak = Math.max(peak, ++inFlight);
      await new Promise((r) => setTimeout(r, 1));
      inFlight--;
      return i;
    });

    await inPool(calls, 3);

    expect(peak).toBe(3);
  });

  it("keeps the calls' order even when they finish out of order", async () => {
    const slow = deferred();
    const fast = deferred();
    const pending = inPool([() => slow.promise, () => fast.promise], 2);

    fast.resolve(2);
    slow.resolve(1);

    expect(await pending).toEqual([
      { status: "fulfilled", value: 1 },
      { status: "fulfilled", value: 2 },
    ]);
  });

  it("records a failed call and goes on with the rest", async () => {
    const error = new Error("network");
    const results = await inPool(
      [() => Promise.reject(error), () => Promise.resolve(2), () => Promise.resolve(3)],
      1,
    );

    expect(results).toEqual([
      { status: "rejected", reason: error },
      { status: "fulfilled", value: 2 },
      { status: "fulfilled", value: 3 },
    ]);
  });
});
