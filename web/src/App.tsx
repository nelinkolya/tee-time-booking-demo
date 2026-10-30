import { useCallback, useEffect, useRef, useState } from "react";
import { cancel, getBookings, getTeeTimes, resetDemo, type Booking, type TeeTime } from "./api";
import { BookingForm } from "./components/BookingForm";
import { BookingList } from "./components/BookingList";
import { ResponseLog } from "./components/ResponseLog";
import { SlotList } from "./components/SlotList";
import { StressPanel } from "./components/StressPanel";
import { VersionFooter } from "./components/VersionFooter";
import { time, today } from "./format";
import { describe, type LogEntry, type LogLine } from "./log";

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

  const addLog = (lines: LogEntry[]) =>
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
        <SlotList date={date} teeTimes={teeTimes} selectedId={selectedId} onSelect={setSelectedId} />

        <section className="detail">
          {selected ? (
            <>
              <h2>
                {time(selected.startsAt)} — {selected.availableSpots} of {selected.capacity} spots free
              </h2>
              <BookingForm teeTime={selected} onResult={(a) => { addLog([describe(a)]); void refresh(); }} />
              <StressPanel teeTime={selected} onResults={(lines) => { addLog(lines); void refresh(); }} />
              <BookingList bookings={bookings} onCancel={async (id) => { await cancel(id); await refresh(); }} />
            </>
          ) : (
            <p className="muted">Pick a tee time.</p>
          )}
        </section>

        <ResponseLog log={log} />
      </main>

      <VersionFooter />
    </div>
  );
}
