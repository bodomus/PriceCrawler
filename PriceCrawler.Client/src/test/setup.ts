import "@testing-library/jest-dom/vitest";

import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";

afterEach(() => {
  cleanup();
  localStorage.clear();
  document.documentElement.classList.remove("dark");
});

// jsdom has no matchMedia; every query is "not matching" unless a test overrides it.
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: vi.fn((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});

// jsdom has no layout: report a fixed size so components that measure themselves (the category tree) render.
class FixedSizeResizeObserver {
  private readonly callback: ResizeObserverCallback;

  /** Stores the callback. */
  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }

  /** Reports a 320×480 box once for the observed element. */
  observe(target: Element): void {
    this.callback([{ target, contentRect: { width: 320, height: 480 } } as unknown as ResizeObserverEntry], this);
  }

  /** Nothing to stop: sizes never change in tests. */
  unobserve(): void {
    // No-op.
  }

  /** Nothing to stop: sizes never change in tests. */
  disconnect(): void {
    // No-op.
  }
}
Object.defineProperty(globalThis, "ResizeObserver", { writable: true, value: FixedSizeResizeObserver });
