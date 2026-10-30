import { useEffect, useState } from "react";
import { book, type BookAttempt, type TeeTime } from "../api";

export function BookingForm({ teeTime, onResult }: { teeTime: TeeTime; onResult: (a: BookAttempt) => void }) {
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
