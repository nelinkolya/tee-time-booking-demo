export const today = () => new Date().toLocaleDateString("sv-SE"); // yyyy-MM-dd in local time
export const time = (iso: string) => iso.slice(11, 16);
