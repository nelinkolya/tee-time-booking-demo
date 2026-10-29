import { useCallback, useEffect, useRef, useState } from "react";
import { book, cancel, getBookings, getTeeTimes, getVersion, resetDemo, type AppVersion, type BookAttempt, type Booking, type TeeTime } from "./api";

const today = () => new Date().toLocaleDateString("sv-SE"); // yyyy-MM-dd in local time
const time = (iso: string) => iso.slice(11, 16);

interface LogLine {
  id: number;
  text: string;
  kind: "ok" | "replay" | "conflict" | "error";
}

function describe(attempt: BookAttempt): Omit<LogLine, "id"> {
  if (attempt.status === 201) return { kind: "ok", text: `201 Created — booking ${attempt.booking!.id.slice(0, 8)}` };
  if (attempt.status === 200 && attempt.replayed)
    return { kind: "replay", text: `200 Replayed — same booking ${attempt.booking!.id.slice(0, 8)}, nothing new booked` };
  if (attempt.status === 409) return { kind: "conflict", text: `409 Conflict — ${attempt.problem?.title} (${attempt.problem?.availableSpots ?? 0} left)` };
  return { kind: "error", text: `${attempt.status} — ${attempt.problem?.title ?? "error"}` };
}

export function App() {
  const [date, setDate] = useState(today);
  const [teeTimes, setTeeTimes] = useState<TeeTime[]>([]);
  const [selectedId, setSelectedId] = useState<number>();
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [log, setLog] = useState<LogLine[]>([]);
  const [error, setError] = useState<string>();
  const nextLogId = useRef(0);

  const selected = teeTimes.find((t) => t.id === selectedId);

  const refresh = useCallback(async () => {
    try {
      setTeeTimes(await getTeeTimes(date));
      if (selectedId !== undefined) setBookings(await getBookings(selectedId));
      setError(undefined);
    } catch (e) {
      setError(String(e));
    }
  }, [date, selectedId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const addLog = (lines: Omit<LogLine, "id">[]) =>
    setLog((prev) => [...lines.map((l) => ({ ...l, id: nextLogId.current++ })), ...prev].slice(0, 60));

  return (
    <div className="page">
      <header>
        <h1>Tee Time Booking</h1>
        <p className="lede">
          A four-ball holds four players. Each booking claims its spots with one conditional UPDATE, and every attempt carries
          an idempotency key — so neither a rush of players nor a double-click can overbook a slot.
        </p>
        <div className="toolbar">
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          {import.meta.env.DEV && (
            <>
              <button
                className="secondary"
                onClick={async () => {
                  await resetDemo();
                  setLog([]);
                  await refresh();
                }}
              >
                Reset demo
              </button>
              <a href="/swagger" target="_blank" rel="noreferrer">
                API docs
              </a>
            </>
          )}
        </div>
        {error && <p className="error">{error}</p>}
      </header>

      <main>
        <section className="slots">
          <h2>Riverside Links · {date}</h2>
          {teeTimes.length === 0 && <p className="muted">No tee times for this day.</p>}
          <ul>
            {teeTimes.map((t) => (
              <li key={t.id}>
                <button className={`slot ${t.id === selectedId ? "selected" : ""} ${t.availableSpots === 0 ? "full" : ""}`} onClick={() => setSelectedId(t.id)}>
                  <span className="slot-time">{time(t.startsAt)}</span>
                  <span className="dots" aria-label={`${t.bookedPlayers} of ${t.capacity} booked`}>
                    {Array.from({ length: t.capacity }, (_, i) => (
                      <span key={i} className={i < t.bookedPlayers ? "dot taken" : "dot"} />
                    ))}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>

        <section className="detail">
          {selected ? (
            <>
              <h2>
                {time(selected.startsAt)} — {selected.availableSpots} of {selected.capacity} spots free
              </h2>
              <BookingForm teeTime={selected} onResult={(a) => { addLog([describe(a)]); void refresh(); }} />
              <StressPanel teeTime={selected} onResults={(lines) => { addLog(lines); void refresh(); }} />
              <h3>Bookings</h3>
              {bookings.length === 0 && <p className="muted">None yet.</p>}
              <ul className="bookings">
                {bookings.map((b) => (
                  <li key={b.id} className={b.status === "Cancelled" ? "cancelled" : ""}>
                    <span>
                      {b.playerName} · {b.players} {b.players === 1 ? "player" : "players"}
                    </span>
                    {b.status === "Confirmed" ? (
                      <button className="link" onClick={async () => { await cancel(b.id); await refresh(); }}>
                        Cancel
                      </button>
                    ) : (
                      <span className="muted">cancelled</span>
                    )}
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <p className="muted">Pick a tee time.</p>
          )}
        </section>

        <section className="log">
          <h2>Server responses</h2>
          {log.length === 0 && <p className="muted">Responses appear here, newest first.</p>}
          <ol>
            {log.map((l) => (
              <li key={l.id} className={l.kind}>
                {l.text}
              </li>
            ))}
          </ol>
        </section>
      </main>

      <VersionFooter />
    </div>
  );
}

const repoUrl = "https://github.com/nelinkolya/tee-time-booking-demo";

function VersionFooter() {
  const [version, setVersion] = useState<AppVersion>();
  useEffect(() => {
    getVersion().then(setVersion, () => undefined);
  }, []);
  if (!version) return null;

  const commit = version.commit?.slice(0, 7);
  const built = version.builtAtUtc && new Date(version.builtAtUtc).toISOString().slice(0, 16).replace("T", " ");
  return (
    <footer className="version">
      v{version.version}
      {commit && (
        <>
          {" · "}
          <a href={`${repoUrl}/commit/${version.commit}`} target="_blank" rel="noreferrer" title={version.commit ?? undefined}>
            {commit}
          </a>
        </>
      )}
      {built && ` · built ${built} UTC`}
      {` · ${version.environment}`}
    </footer>
  );
}

function BookingForm({ teeTime, onResult }: { teeTime: TeeTime; onResult: (a: BookAttempt) => void }) {
  const [name, setName] = useState("Alex");
  const [players, setPlayers] = useState(2);
  // One key per attempt: a second click replays; a new key only for a different attempt or after success.
  const [key, setKey] = useState(() => crypto.randomUUID());
  useEffect(() => setKey(crypto.randomUUID()), [teeTime.id, name, players]);

  return (
    <form
      className="book-form"
      onSubmit={async (e) => {
        e.preventDefault();
        // The button is not disabled while sending: double-clicks are safe.
        const attempt = await book(teeTime.id, name, players, key);
        onResult(attempt);
        if (attempt.status === 201 || attempt.status === 200) setKey(crypto.randomUUID());
      }}
    >
      <label>
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} />
      </label>
      <label>
        Players
        <select value={players} onChange={(e) => setPlayers(Number(e.target.value))}>
          {[1, 2, 3, 4].map((n) => (
            <option key={n}>{n}</option>
          ))}
        </select>
      </label>
      <button type="submit">Book</button>
      <span className="key" title="Idempotency-Key for this attempt">key {key.slice(0, 8)}</span>
    </form>
  );
}

/** Runs the calls with at most `limit` in flight; results keep the calls' order. */
async function inPool<T>(calls: (() => Promise<T>)[], limit: number): Promise<PromiseSettledResult<T>[]> {
  const results: PromiseSettledResult<T>[] = new Array(calls.length);
  let next = 0;
  const worker = async () => {
    while (next < calls.length) {
      const i = next++;
      try {
        results[i] = { status: "fulfilled", value: await calls[i]() };
      } catch (reason) {
        results[i] = { status: "rejected", reason };
      }
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, calls.length) }, worker));
  return results;
}

function StressPanel({ teeTime, onResults }: { teeTime: TeeTime; onResults: (lines: Omit<LogLine, "id">[]) => void }) {
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
