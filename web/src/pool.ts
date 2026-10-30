/** Runs the calls with at most `limit` in flight; results keep the calls' order. */
export async function inPool<T>(calls: (() => Promise<T>)[], limit: number): Promise<PromiseSettledResult<T>[]> {
  const results: PromiseSettledResult<T>[] = new Array(calls.length);
  let next = 0;
  const worker = async () => {
    while (next < calls.length) {
      const i = next++;
      try {
        results[i] = { status: "fulfilled", value: await calls[i]() };
      } catch (reason) {
        results[i] = { status: "rejected", reason };
      }
    }
  };
  await Promise.all(Array.from({ length: Math.min(limit, calls.length) }, worker));
  return results;
}
