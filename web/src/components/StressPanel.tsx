import { useState } from "react";
import { book, type BookAttempt, type TeeTime } from "../api";
import type { LogEntry } from "../log";
import { inPool } from "../pool";

export function StressPanel({ teeTime, onResults }: { teeTime: TeeTime; onResults: (lines: LogEntry[]) => void }) {
  const [busy, setBusy] = useState(false);
  const [rush, setRush] = useState(1000);

  const run = async (label: string, attempts: (() => Promise<BookAttempt>)[]) => {
    setBusy(true);
    const started = performance.now();
    try {
      const settled = await inPool(attempts, 200);
      const results = settled.flatMap((r) => (r.status === "fulfilled" ? [r.value] : []));
      const networkErrors = settled.length - results.length;
      const seconds = ((performance.now() - started) / 1000).toFixed(1);
      const count = (s: number, replayed = false) => results.filter((r) => r.status === s && r.replayed === replayed).length;
      const bookingIds = new Set(results.flatMap((r) => (r.booking ? [r.booking.id] : [])));
      onResults([
        {
          kind: "ok",
          text:
            `${label}: ${settled.length} requests in ${seconds}s → ${count(201)} created, ${count(200, true)} replayed, ` +
            `${count(409)} conflict; ${bookingIds.size} distinct booking(s)` +
            (networkErrors > 0 ? `; ${networkErrors} network error(s)` : ""),
        },
      ]);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="stress">
      <h3>Try to break it</h3>
      <select value={rush} onChange={(e) => setRush(Number(e.target.value))} disabled={busy} aria-label="Players in the rush">
        {[20, 1000].map((n) => (
          <option key={n} value={n}>
            {n.toLocaleString("en")}
          </option>
        ))}
      </select>
      <button
        disabled={busy}
        onClick={() =>
          run("Rush", Array.from({ length: rush }, (_, i) => () => book(teeTime.id, `Rusher ${i + 1}`, 1, crypto.randomUUID())))
        }
      >
        {busy ? "Sending…" : "players at once"}
      </button>
      <button
        disabled={busy}
        onClick={() => {
          const key = crypto.randomUUID();
          return run("Double-submit", Array.from({ length: 10 }, () => () => book(teeTime.id, "Impatient", 2, key)));
        }}
      >
        Same request ×10
      </button>
      <p className="muted">
        The first sends that many different one-player bookings in parallel: only the free spots get filled. The second
        sends one two-player booking ten times with the same key: one booking, nine replays. The page keeps at most 200
        requests in flight, since a browser tab fails when it holds too many open at once.
      </p>
    </div>
  );
}
