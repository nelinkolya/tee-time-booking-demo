import type { TeeTime } from "../../api";
import { time } from "../../format";
import styles from "./SlotList.module.css";

export function SlotList({ date, teeTimes, selectedId, onSelect }: { date: string; teeTimes: TeeTime[]; selectedId?: number; onSelect: (id: number) => void }) {
  return (
    <section>
      <h2>Riverside Links · {date}</h2>
      {teeTimes.length === 0 && <p className="muted">No tee times for this day.</p>}
      <ul className={styles.list}>
        {teeTimes.map((t) => (
          <li key={t.id}>
            <button
              className={[styles.slot, t.id === selectedId && styles.selected, t.availableSpots === 0 && styles.full].filter(Boolean).join(" ")}
              onClick={() => onSelect(t.id)}
            >
              <span className={styles.time}>{time(t.startsAt)}</span>
              <span className={styles.dots} aria-label={`${t.bookedPlayers} of ${t.capacity} booked`}>
                {Array.from({ length: t.capacity }, (_, i) => (
                  <span key={i} className={i < t.bookedPlayers ? `${styles.dot} ${styles.taken}` : styles.dot} />
                ))}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
