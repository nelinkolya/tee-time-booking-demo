import type { BookAttempt } from "./api";

export interface LogLine {
  id: number;
  text: string;
  kind: "ok" | "replay" | "conflict" | "error";
}

export type LogEntry = Omit<LogLine, "id">;

export function describe(attempt: BookAttempt): LogEntry {
  if (attempt.status === 201) return { kind: "ok", text: `201 Created — booking ${attempt.booking!.id.slice(0, 8)}` };
  if (attempt.status === 200 && attempt.replayed)
    return { kind: "replay", text: `200 Replayed — same booking ${attempt.booking!.id.slice(0, 8)}, nothing new booked` };
  if (attempt.status === 409) return { kind: "conflict", text: `409 Conflict — ${attempt.problem?.title} (${attempt.problem?.availableSpots ?? 0} left)` };
  return { kind: "error", text: `${attempt.status} — ${attempt.problem?.title ?? "error"}` };
}
