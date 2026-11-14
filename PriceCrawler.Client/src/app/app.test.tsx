import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { category, page, product } from "@/test/fixtures";
import { renderApp, stubFetch } from "@/test/render";

const categories = [category(1, "Молочные продукты", { hasChildren: true, productCount: 1234 })];

describe("app shell", () => {
  beforeEach(() => {
    // Desktop width: every media query in the app is a min-width one.
    vi.spyOn(window, "matchMedia").mockImplementation(
      (query: string) =>
        ({
          matches: query.includes("min-width"),
          media: query,
          addEventListener: vi.fn(),
          removeEventListener: vi.fn(),
        }) as unknown as MediaQueryList,
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("redirects / to products with the category tree and the product table", async () => {
    stubFetch({
      "/health": "Healthy",
      "/api/v1/stores/varus/categories": categories,
      "/api/v1/stores/varus/products": page([product(1, "Молоко 2,5%")]),
    });
    renderApp("/");

    expect(await screen.findByText("Молочные продукты")).toBeInTheDocument();
    expect(await screen.findByText("Молоко 2,5%")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Товары" })).toBeInTheDocument();
  });

  it("shows the four sections in the side menu", () => {
    stubFetch({});
    renderApp("/products");
    const nav = screen.getByRole("navigation", { name: "Разделы" });
    const links = within(nav)
      .getAllByRole("link")
      .map((link) => link.textContent);
    expect(links).toEqual(["Товары", "Аналитика", "Разработка", "Пользователи"]);
  });

  it("navigates to a placeholder section", async () => {
    stubFetch({});
    renderApp("/products");
    await userEvent.click(screen.getByRole("link", { name: "Аналитика" }));
    expect(screen.getByRole("heading", { level: 2, name: "Аналитика" })).toBeInTheDocument();
    expect(screen.getByText("Раздел в разработке")).toBeInTheDocument();
  });

  it("shows an error with retry when the API fails", async () => {
    stubFetch({});
    renderApp("/products");
    expect((await screen.findAllByRole("alert"))[0]).toHaveTextContent("Не удалось загрузить данные");
  });

  it("shows 404 for an unknown address", () => {
    stubFetch({});
    renderApp("/nope");
    expect(screen.getByText("Страница не найдена")).toBeInTheDocument();
  });
});
