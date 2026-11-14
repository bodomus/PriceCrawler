import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { category, page, product } from "@/test/fixtures";
import { renderApp, stubFetch } from "@/test/render";

const roots = [
  category(1, "Овощи и фрукты", { hasChildren: true, productCount: 3 }),
  category(2, "Молочные продукты", { productCount: 2 }),
];
const children = [category(11, "Картофель", { parentId: 1, productCount: 2 })];

/** Every products request the page made, as URLs. */
let productRequests: URL[];

/**
 * Stubs the API: category levels and a products endpoint that echoes nothing but records its requests.
 * @param productsResponse What the products endpoint returns.
 */
function stubApi(productsResponse = page([])) {
  productRequests = [];
  stubFetch({
    "/health": "Healthy",
    "/api/v1/stores/varus/categories": (url: URL) => (url.searchParams.get("parentId") === "1" ? children : roots),
    "/api/v1/stores/varus/products": (url: URL) => {
      productRequests.push(url);
      return productsResponse;
    },
  });
}

/**
 * Last products request.
 * @returns Its URL.
 */
function lastRequest(): URL {
  const url = productRequests.at(-1);
  if (url === undefined) throw new Error("No products request was made.");
  return url;
}

describe("products page", () => {
  beforeEach(() => {
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

  it("shows price, day change, promo and old price", async () => {
    stubApi(
      page([
        product(1, "Картофель белый", {
          price: 25.5,
          oldPrice: 30,
          isPromo: true,
          previousPrice: 30,
          priceChange: -4.5,
          priceChangePercent: -15,
          trend: [30, 30, 25.5],
        }),
        product(2, "Картофель розовый", { price: 34.9, priceChange: 1.9, priceChangePercent: 5.76, inStock: false }),
      ]),
    );
    renderApp("/products");

    const white = (await screen.findByText("Картофель белый")).closest("tr");
    expect(white).not.toBeNull();
    const row = within(white as HTMLElement);
    expect(row.getByText("Акция")).toBeInTheDocument();
    expect(row.getByText("25,50")).toBeInTheDocument();
    expect(row.getByText("30,00")).toHaveClass("line-through");
    expect(row.getByTitle("Цена снизилась")).toHaveAttribute("data-direction", "down");
    expect(row.getByRole("img", { name: "30 дней" })).toBeInTheDocument();

    const pink = within(screen.getByText("Картофель розовый").closest("tr") as HTMLElement);
    expect(pink.getByTitle("Цена выросла")).toHaveAttribute("data-direction", "up");
    expect(pink.getByText("Нет в наличии")).toBeInTheDocument();
  });

  it("loads children of a category when it is opened and filters by it", async () => {
    stubApi();
    renderApp("/products");

    await userEvent.click(await screen.findByText("Овощи и фрукты"));

    expect(await screen.findByText("Картофель")).toBeInTheDocument();
    await waitFor(() => {
      expect(lastRequest().searchParams.get("categoryId")).toBe("1");
    });
    expect(screen.getByTestId("list-title")).toHaveTextContent("Овощи и фрукты");
  });

  it("sends search, sort and grouping to the server and keeps them in the address", async () => {
    stubApi(page([product(1, "Молоко")]));
    renderApp("/products?q=молоко&sort=-priceChange&group=change&page=2");

    await screen.findByText("Молоко");
    const url = lastRequest();
    expect(url.searchParams.get("filter")).toBe("name:contains:молоко");
    expect(url.searchParams.get("sort")).toBe("-priceChange");
    expect(url.searchParams.get("groupBy")).toBe("change");
    expect(url.searchParams.get("page")).toBe("2");
    expect(screen.getByRole("searchbox")).toHaveValue("молоко");
  });

  it("sorts by a column header click", async () => {
    stubApi(page([product(1, "Молоко")]));
    renderApp("/products");

    await userEvent.click(await screen.findByRole("button", { name: /Цена, ₴/ }));

    await waitFor(() => {
      expect(lastRequest().searchParams.get("sort")).toBe("price");
    });
    await userEvent.click(screen.getByRole("button", { name: /Цена, ₴/ }));
    await waitFor(() => {
      expect(lastRequest().searchParams.get("sort")).toBe("-price");
    });
  });

  it("debounces the search box", async () => {
    stubApi(page([product(1, "Молоко")]));
    renderApp("/products");
    await screen.findByText("Молоко");

    await userEvent.type(screen.getByRole("searchbox"), "сыр");

    await waitFor(() => {
      expect(lastRequest().searchParams.get("filter")).toBe("name:contains:сыр");
    });
    expect(productRequests.some((url) => url.searchParams.get("filter") === "name:contains:с")).toBe(false);
  });

  it("draws a header row for every group", async () => {
    stubApi(
      page(
        [
          product(1, "Сыр", { groupKey: "down", priceChange: -1 }),
          product(2, "Масло", { groupKey: "up", priceChange: 2 }),
          product(3, "Кефир", { groupKey: "up", priceChange: 1 }),
        ],
        {
          groups: [
            { key: "down", name: null, count: 1 },
            { key: "up", name: null, count: 7 },
          ],
        },
      ),
    );
    renderApp("/products?group=change");

    await screen.findByText("Сыр");
    const groups = screen.getAllByTestId("group-row").map((row) => row.textContent);
    expect(groups).toEqual(["Цена снизилась1 товар", "Цена выросла7 товаров"]);
  });

  it("pages forward", async () => {
    stubApi(page([product(1, "Молоко")], { totalCount: 120, totalPages: 3 }));
    renderApp("/products");
    await screen.findByText("Молоко");
    expect(screen.getByText("1–50 из 120")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Следующая страница" }));

    await waitFor(() => {
      expect(lastRequest().searchParams.get("page")).toBe("2");
    });
  });
});
