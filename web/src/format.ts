/** Today's local date as yyyy-MM-dd — the format <input type="date"> uses. Not toISOString(): that is UTC. */
export const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
export const time = (iso: string) => iso.slice(11, 16);
