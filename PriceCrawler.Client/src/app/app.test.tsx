import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { CategoryNode } from "@/api/client";
import { renderApp, stubFetch } from "@/test/render";

const categories: CategoryNode[] = [
  {
    id: 1,
    externalId: "1",
    name: "Молочные продукты",
    url: "https://varus.ua/moloko",
    parentId: null,
    hasChildren: true,
    productCount: 1234,
  },
];

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

  it("redirects / to products and lists top-level categories from the API", async () => {
    const fetchMock = stubFetch({ "/health": "Healthy", "/api/v1/stores/varus/categories": categories });
    renderApp("/");

    expect(await screen.findByText("Молочные продукты")).toBeInTheDocument();
    expect(screen.getByText("1 234 товара")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Товары" })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalled();
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
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось загрузить данные");
  });

  it("shows 404 for an unknown address", () => {
    stubFetch({});
    renderApp("/nope");
    expect(screen.getByText("Страница не найдена")).toBeInTheDocument();
  });
});
