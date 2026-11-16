import { useRef, useState } from "react";
import { cancel, getTicket, resetDemo } from "./api";
import styles from "./App.module.css";
import { BookingForm } from "./components/BookingForm/BookingForm";
import { BookingList } from "./components/BookingList/BookingList";
import { ResponseLog } from "./components/ResponseLog/ResponseLog";
import { SlotList } from "./components/SlotList/SlotList";
import { StressPanel } from "./components/StressPanel/StressPanel";
import { TicketPanel } from "./components/TicketPanel/TicketPanel";
import { VersionFooter } from "./components/VersionFooter/VersionFooter";
import { time, today } from "./format";
import { useTeeTimes } from "./hooks/useTeeTimes";
import { describe, type LogEntry, type LogLine } from "./log";

export function App() {
  const [date, setDate] = useState(today);
  const [selectedId, setSelectedId] = useState<number>();
  const [log, setLog] = useState<LogLine[]>([]);
  // A counter with the token, so asking twice for the same ticket still resets the panel.
  const [ticket, setTicket] = useState<{ token: string; n: number }>();
  const nextLogId = useRef(0);
  const { teeTimes, bookings, error, refresh, change } = useTeeTimes(date, selectedId);

  const selected = teeTimes.find((t) => t.id === selectedId);

  const addLog = (lines: LogEntry[]) =>
    setLog((prev) => [...lines.map((l) => ({ ...l, id: nextLogId.current++ })), ...prev].slice(0, 60));

  const showTicket = async (bookingId: string) => {
    try {
      const r = await getTicket(bookingId);
      if (r.ok) {
        setTicket((t) => ({ token: r.token, n: (t?.n ?? 0) + 1 }));
        addLog([{ kind: "ok", text: `200 Ticket — booking ${bookingId.slice(0, 8)}` }]);
      } else {
        addLog([{ kind: r.status === 409 ? "conflict" : "error", text: `${r.status} ${r.title}` }]);
      }
    } catch (e) {
      addLog([{ kind: "error", text: String(e) }]);
    }
  };

  return (
    <div className={styles.page}>
      <header>
        <h1>Tee Time Booking</h1>
        <p className={styles.lede}>
          A four-ball holds four players. Each booking claims its spots with one conditional UPDATE, and every attempt carries
          an idempotency key — so neither a rush of players nor a double-click can overbook a slot.
        </p>
        <div className={styles.toolbar}>
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          {import.meta.env.DEV && (
            <>
              <button className="secondary" onClick={() => void change(async () => { await resetDemo(); setLog([]); })}>
                Reset demo
              </button>
              <a href="/swagger" target="_blank" rel="noreferrer">
                API docs
              </a>
            </>
          )}
        </div>
        {error && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}
      </header>

      <main className={styles.columns}>
        <SlotList date={date} teeTimes={teeTimes} selectedId={selectedId} onSelect={setSelectedId} />

        <section>
          {selected ? (
            <>
              <h2>
                {time(selected.startsAt)} — {selected.availableSpots} of {selected.capacity} spots free
              </h2>
              {/* A different tee time is a different attempt: a new form, so a new idempotency key. */}
              <BookingForm key={selected.id} teeTime={selected} onResult={(a) => { addLog([describe(a)]); void refresh(); }} />
              <StressPanel teeTime={selected} onResults={(lines) => { addLog(lines); void refresh(); }} />
              <BookingList
                bookings={bookings}
                onCancel={(id) => void change(() => cancel(id))}
                onTicket={(id) => void showTicket(id)}
              />
            </>
          ) : (
            <p className="muted">Pick a tee time.</p>
          )}
          <TicketPanel key={ticket?.n ?? 0} token={ticket?.token} onLog={(entry) => addLog([entry])} />
        </section>

        <ResponseLog log={log} />
      </main>

      <VersionFooter />
    </div>
  );
}
