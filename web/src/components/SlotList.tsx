import type { TeeTime } from "../api";
import { time } from "../format";

export function SlotList({ date, teeTimes, selectedId, onSelect }: { date: string; teeTimes: TeeTime[]; selectedId?: number; onSelect: (id: number) => void }) {
  return (
    <section className="slots">
      <h2>Riverside Links · {date}</h2>
      {teeTimes.length === 0 && <p className="muted">No tee times for this day.</p>}
      <ul>
        {teeTimes.map((t) => (
          <li key={t.id}>
            <button className={`slot ${t.id === selectedId ? "selected" : ""} ${t.availableSpots === 0 ? "full" : ""}`} onClick={() => onSelect(t.id)}>
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
  );
}
