import { useState } from "react";
import { book, type BookAttempt, type TeeTime } from "../../api";
import styles from "./BookingForm.module.css";

export function BookingForm({ teeTime, onResult }: { teeTime: TeeTime; onResult: (a: BookAttempt) => void }) {
  const [name, setName] = useState("Alex");
  const [players, setPlayers] = useState(2);
  // One key per attempt: a second click replays; a new key only for a different attempt or after success.
  // A different tee time remounts the form (App gives it the tee time's id as its React key).
  const [key, setKey] = useState(() => crypto.randomUUID());
  const newAttempt = () => setKey(crypto.randomUUID());

  const submit = async () => {
    // The button is not disabled while sending: double-clicks are safe.
    let attempt: BookAttempt;
    try {
      attempt = await book(teeTime.id, name, players, key);
    } catch (e) {
      // No answer (network down): the same key is kept, so trying again is still a retry.
      onResult({ status: 0, replayed: false, problem: { title: String(e) } });
      return;
    }
    onResult(attempt);
    if (attempt.status === 201 || attempt.status === 200) newAttempt();
  };

  return (
    <form
      className={styles.form}
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <label>
        Name
        <input value={name} onChange={(e) => {
            setName(e.target.value);
            newAttempt();
          }} required maxLength={100} />
      </label>
      <label>
        Players
        <select value={players} onChange={(e) => {
            setPlayers(Number(e.target.value));
            newAttempt();
          }}>
          {[1, 2, 3, 4].map((n) => (
            <option key={n}>{n}</option>
          ))}
        </select>
      </label>
      <button type="submit">Book</button>
      <span className={styles.key} title="Idempotency-Key for this attempt">key {key.slice(0, 8)}</span>
    </form>
  );
}
