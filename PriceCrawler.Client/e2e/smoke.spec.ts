import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  // The smoke test checks the built site, not the API: answer API calls with fixtures.
  await page.route("**/health", (route) => route.fulfill({ status: 200, body: "Healthy" }));
  await page.route("**/api/v1/stores/varus/categories**", (route) =>
    route.fulfill({
      json: [
        {
          id: 1,
          externalId: "1",
          name: "Овощи и фрукты",
          url: "https://varus.ua/ovoshchi",
          parentId: null,
          hasChildren: true,
          productCount: 812,
        },
      ],
    }),
  );
  await page.route("**/api/v1/stores/varus/categories?parentId=1", (route) =>
    route.fulfill({
      json: [
        {
          id: 11,
          externalId: "11",
          name: "Картофель",
          url: "https://varus.ua/kartoplia",
          parentId: 1,
          hasChildren: false,
          productCount: 2,
        },
      ],
    }),
  );
  await page.route("**/api/v1/stores/varus/products**", (route) =>
    route.fulfill({
      json: {
        items: [
          {
            productId: 1,
            externalId: "1001",
            name: "Картофель белый",
            url: "https://varus.ua/kartoplia-bila",
            packValue: 1,
            packUnit: "кг",
            price: 25.5,
            oldPrice: 30,
            isPromo: true,
            inStock: true,
            capturedAt: "2026-11-14T06:00:00Z",
            previousPrice: 30,
            priceChange: -4.5,
            priceChangePercent: -15,
            trend: [30, 31, 25.5],
            groupKey: null,
          },
        ],
        page: 1,
        pageSize: 50,
        totalCount: 1,
        totalPages: 1,
        groups: [],
      },
    }),
  );
});

test("shows products with day change and opens a category", async ({ page }) => {
  await page.goto("/products");

  const row = page.getByRole("row", { name: /Картофель белый/ });
  await expect(row).toContainText("25,50");
  await expect(row.getByTitle("Цена снизилась")).toBeVisible();
  await expect(row.getByText("Акция")).toBeVisible();

  await page.getByRole("treeitem", { name: /Овощи и фрукты/ }).click();
  await expect(page.getByRole("treeitem", { name: /Картофель/ })).toBeVisible();
  await expect(page).toHaveURL(/category=1/);
});

test("opens products, switches section and theme", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/products$/);
  await expect(page.getByText("Овощи и фрукты").first()).toBeVisible();
  await expect(page.getByTestId("kyiv-clock")).toContainText("Киев");
  await expect(page.getByRole("status", { name: "API работает" })).toBeVisible();

  await page.getByRole("link", { name: "Разработка" }).click();
  await expect(page.getByRole("heading", { level: 2, name: "Разработка" })).toBeVisible();

  await page.getByRole("button", { name: "Тема" }).click();
  await page.getByRole("menuitemradio", { name: "Тёмная" }).click();
  await expect(page.locator("html")).toHaveClass(/dark/);

  await page.reload();
  await expect(page.locator("html")).toHaveClass(/dark/);
});

test("page itself does not scroll, only the content panel", async ({ page }) => {
  await page.goto("/products");
  await expect(page.getByText("Овощи и фрукты").first()).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight);
  expect(overflow).toBeLessThanOrEqual(0);
});
