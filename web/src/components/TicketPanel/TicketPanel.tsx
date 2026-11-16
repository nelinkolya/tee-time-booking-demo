import { useState } from "react";
import { verifyTicket, type VerifyResult } from "../../api";
import { time } from "../../format";
import type { LogEntry } from "../../log";
import { decodeTicket, tamper } from "../../ticket";
import styles from "./TicketPanel.module.css";

const reasons: Record<string, string> = {
  BadSignature: "it was changed after it was signed",
  UnknownKey: "it names a key version this service does not have, or has retired",
  Malformed: "it is not a ticket",
  BookingNotFound: "its booking no longer exists",
};

/**
 * A ticket to look at and check: the one just issued for a booking, or any pasted in.
 * The parent remounts it (key) for each new ticket, so the text starts as that ticket.
 */
export function TicketPanel({ token, onLog }: { token?: string; onLog: (entry: LogEntry) => void }) {
  const [text, setText] = useState(token ?? "");
  const [result, setResult] = useState<VerifyResult>();
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);

  const decoded = decodeTicket(text);

  const edit = (value: string) => {
    setText(value);
    setResult(undefined);
    setCopied(false);
  };

  const verify = async () => {
    setBusy(true);
    try {
      const r = await verifyTicket(text.trim());
      setResult(r);
      onLog(
        r.ok
          ? { kind: r.check.status === "Confirmed" ? "ok" : "replay", text: `200 Valid ticket — booking ${r.check.bookingId.slice(0, 8)}, ${r.check.status}` }
          : { kind: r.status === 422 ? "conflict" : "error", text: `${r.status} ${r.title}${r.reason ? ` — ${r.reason}` : ""}` },
      );
    } catch (e) {
      onLog({ kind: "error", text: String(e) });
    } finally {
      setBusy(false);
    }
  };

  const copy = async () => {
    await navigator.clipboard.writeText(text.trim());
    setCopied(true);
  };

  return (
    <div className={styles.panel}>
      <h3>Ticket</h3>
      <p className="muted">
        A JWT signed with ES256 by a key that stays in Key Vault (in memory when run locally). It states the booking; anyone can
        check it against the{" "}
        <a href="/.well-known/jwks.json" target="_blank" rel="noreferrer">
          public keys
        </a>
        . Get one from a booking above, or paste one here.
      </p>

      <textarea
        aria-label="Ticket"
        className={styles.token}
        rows={4}
        spellCheck={false}
        value={text}
        placeholder="eyJhbGciOiJFUzI1NiIs…"
        onChange={(e) => edit(e.target.value)}
      />

      {decoded && (
        <dl className={styles.claims}>
          <dt>Booking</dt>
          <dd>{decoded.bookingId?.slice(0, 8)}</dd>
          <dt>Players</dt>
          <dd>{decoded.players}</dd>
          <dt>Tee time</dt>
          <dd>{decoded.startsAt ? time(decoded.startsAt) : "—"}</dd>
          <dt>Key version</dt>
          <dd title={decoded.kid}>{decoded.kid?.slice(0, 8)}</dd>
        </dl>
      )}

      <div className={styles.actions}>
        <button onClick={() => void verify()} disabled={busy || text.trim() === ""}>
          Verify
        </button>
        <button
          className="secondary"
          onClick={() => edit(tamper(text))}
          disabled={!decoded}
          title="Claim a different number of players, keeping the signature"
        >
          Change a byte
        </button>
        <button className="secondary" onClick={() => void copy()} disabled={text.trim() === ""}>
          {copied ? "Copied" : "Copy"}
        </button>
      </div>

      {result && (
        <p role="status" className={result.ok ? (result.check.status === "Confirmed" ? styles.valid : styles.warning) : styles.invalid}>
          {result.ok
            ? result.check.status === "Confirmed"
              ? `✓ Valid — ${result.check.players} ${result.check.players === 1 ? "player" : "players"} at ${time(result.check.startsAt)}, booking confirmed.`
              : `✓ Validly signed — but the booking has been cancelled since.`
            : result.status === 422
              ? `✗ Not a valid ticket: ${reasons[result.reason ?? ""] ?? result.title}.`
              : `${result.title}`}
        </p>
      )}
    </div>
  );
}
