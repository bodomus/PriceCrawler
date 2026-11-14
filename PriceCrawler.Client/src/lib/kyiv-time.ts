/** IANA time zone the whole site shows dates in, whatever the browser's zone is. */
export const KYIV_TIME_ZONE = "Europe/Kyiv";

const dateTimeFormat = new Intl.DateTimeFormat("ru-RU", {
  timeZone: KYIV_TIME_ZONE,
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

const timeFormat = new Intl.DateTimeFormat("ru-RU", {
  timeZone: KYIV_TIME_ZONE,
  hour: "2-digit",
  minute: "2-digit",
});

/**
 * Formats an instant as Kyiv local date and time, e.g. `14.11.2026, 07:45`.
 * @param value Date or ISO 8601 string (API dates are UTC).
 * @returns Formatted text.
 */
export function formatKyivDateTime(value: Date | string): string {
  return dateTimeFormat.format(typeof value === "string" ? new Date(value) : value);
}

/**
 * Formats an instant as Kyiv local time of day, e.g. `07:45`.
 * @param value Date to format.
 * @returns Formatted text.
 */
export function formatKyivTime(value: Date): string {
  return timeFormat.format(value);
}
