import type { Booking } from "../api";

export function BookingList({ bookings, onCancel }: { bookings: Booking[]; onCancel: (id: string) => void }) {
  return (
    <>
      <h3>Bookings</h3>
      {bookings.length === 0 && <p className="muted">None yet.</p>}
      <ul className="bookings">
        {bookings.map((b) => (
          <li key={b.id} className={b.status === "Cancelled" ? "cancelled" : ""}>
            <span>
              {b.playerName} · {b.players} {b.players === 1 ? "player" : "players"}
            </span>
            {b.status === "Confirmed" ? (
              <button className="link" onClick={() => onCancel(b.id)}>
                Cancel
              </button>
            ) : (
              <span className="muted">cancelled</span>
            )}
          </li>
        ))}
      </ul>
    </>
  );
}
