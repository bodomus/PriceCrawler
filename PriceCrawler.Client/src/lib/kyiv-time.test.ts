import { describe, expect, it } from "vitest";

import { formatKyivDateTime, formatKyivTime } from "./kyiv-time";

describe("kyiv-time", () => {
  it("shows UTC instants in Kyiv winter time (UTC+2)", () => {
    expect(formatKyivDateTime("2026-11-14T05:45:00Z")).toBe("14.11.2026, 07:45");
  });

  it("shows UTC instants in Kyiv summer time (UTC+3)", () => {
    expect(formatKyivTime(new Date("2026-07-01T21:30:00Z"))).toBe("00:30");
  });
});
