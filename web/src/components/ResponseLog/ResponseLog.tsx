import type { LogLine } from "../../log";
import styles from "./ResponseLog.module.css";

export function ResponseLog({ log }: { log: LogLine[] }) {
  return (
    <section>
      <h2>Server responses</h2>
      {log.length === 0 && <p className="muted">Responses appear here, newest first.</p>}
      <ol className={styles.list}>
        {log.map((l) => (
          <li key={l.id} className={styles[l.kind]}>
            {l.text}
          </li>
        ))}
      </ol>
    </section>
  );
}
