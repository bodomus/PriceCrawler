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
});

test("opens products, switches section and theme", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/products$/);
  await expect(page.getByText("Овощи и фрукты")).toBeVisible();
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
  await expect(page.getByText("Овощи и фрукты")).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight);
  expect(overflow).toBeLessThanOrEqual(0);
});
