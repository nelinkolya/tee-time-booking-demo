import { useCallback, useEffect, useState } from "react";
import { getBookings, getTeeTimes, type Booking, type TeeTime } from "../api";

/** The tee times of a day and the bookings of the selected one, as the server has them. */
export function useTeeTimes(date: string, selectedId: number | undefined) {
  const [teeTimes, setTeeTimes] = useState<TeeTime[]>([]);
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [error, setError] = useState<string>();

  const refresh = useCallback(async (signal?: AbortSignal) => {
    try {
      setTeeTimes(await getTeeTimes(date, signal));
      if (selectedId !== undefined) setBookings(await getBookings(selectedId, signal));
      setError(undefined);
    } catch (e) {
      if (signal?.aborted) return; // superseded by a newer refresh, not an error
      setError(String(e));
    }
  }, [date, selectedId]);

  // A newer date or tee time aborts the request still loading, so a slow stale response cannot overwrite it.
  useEffect(() => {
    const controller = new AbortController();
    // oxlint-disable-next-line react/set-state-in-effect -- syncing with the server: refresh sets state only once it answers
    void refresh(controller.signal);
    return () => controller.abort();
  }, [refresh]);

  /**
   * Makes a change on the server, then reads the state again rather than patching it here: other players book too.
   * Not awaited by event handlers, so it shows its own failure instead of losing it.
   */
  const change = async (action: () => Promise<unknown>) => {
    try {
      await action();
      await refresh();
    } catch (e) {
      setError(String(e));
    }
  };

  return { teeTimes, bookings, error, refresh, change };
}
