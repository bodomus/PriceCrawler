import { useEffect, useState } from "react";

/**
 * Returns a value that follows the input only after it has stopped changing for a while.
 * @param value Value to follow.
 * @param delayMs Quiet time in milliseconds.
 * @returns The debounced value.
 */
export function useDebouncedValue<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = window.setTimeout(() => {
      setDebounced(value);
    }, delayMs);
    return () => {
      window.clearTimeout(id);
    };
  }, [value, delayMs]);
  return debounced;
}
