import { useEffect, useState } from "react";

/**
 * Current time that re-renders the caller periodically.
 * @param intervalMs How often to refresh, in milliseconds.
 * @returns Current date.
 */
export function useNow(intervalMs = 15_000): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const id = window.setInterval(() => {
      setNow(new Date());
    }, intervalMs);
    return () => {
      window.clearInterval(id);
    };
  }, [intervalMs]);
  return now;
}
