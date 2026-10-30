import type { LogLine } from "../log";

export function ResponseLog({ log }: { log: LogLine[] }) {
  return (
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
  );
}
