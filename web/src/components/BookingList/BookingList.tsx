import type { Booking } from "../../api";
import styles from "./BookingList.module.css";

export function BookingList({
  bookings,
  onCancel,
  onTicket,
}: {
  bookings: Booking[];
  onCancel: (id: string) => void;
  onTicket: (id: string) => void;
}) {
  return (
    <>
      <h3>Bookings</h3>
      {bookings.length === 0 && <p className="muted">None yet.</p>}
      <ul className={styles.list}>
        {bookings.map((b) => (
          <li key={b.id} className={b.status === "Cancelled" ? styles.cancelled : undefined}>
            <span>
              {b.playerName} · {b.players} {b.players === 1 ? "player" : "players"}
            </span>
            {b.status === "Confirmed" ? (
              <span className={styles.actions}>
                <button className={`link ${styles.ticket}`} onClick={() => onTicket(b.id)}>
                  Ticket
                </button>
                <button className="link" onClick={() => onCancel(b.id)}>
                  Cancel
                </button>
              </span>
            ) : (
              <span className="muted">cancelled</span>
            )}
          </li>
        ))}
      </ul>
    </>
  );
}
