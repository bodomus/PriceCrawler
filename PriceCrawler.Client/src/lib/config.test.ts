import { describe, expect, it } from "vitest";

import { resolveApiBaseUrl } from "./config";

describe("resolveApiBaseUrl", () => {
  it("uses the page origin when nothing is configured", () => {
    expect(resolveApiBaseUrl(undefined)).toBe(globalThis.location.origin);
    expect(resolveApiBaseUrl("  ")).toBe(globalThis.location.origin);
  });

  it("drops trailing slashes of a configured address", () => {
    expect(resolveApiBaseUrl("http://localhost:5280//")).toBe("http://localhost:5280");
  });
});
