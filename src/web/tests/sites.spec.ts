import { expect, test } from "@playwright/test";
import type { Status } from "../src/api";

test("work sites normalize, persist, survive other settings and can be removed", async ({
  page,
  request,
}, info) => {
  test.skip(
    !process.env.WLB_TEST_URL || process.env.WLB_TEST_URL.endsWith(":47831"),
    "Use an isolated test database",
  );
  const initial = (await (
    await request.get("/api/v1/status")
  ).json()) as Status;
  const session = (await (await request.get("/api/v1/session")).json()) as {
    token: string;
  };
  const headers = { "X-WLB-Token": session.token };
  try {
    await page.goto("/");
    const section = page.getByRole("region", { name: "Рабочие сайты" });
    await section
      .getByLabel("Домен рабочего сайта")
      .fill("https://DOCS.Example.com/lesson?private=secret#part");
    await section.getByRole("button", { name: "Добавить сайт" }).click();
    const remove = section.getByRole("button", {
      name: "Удалить docs.example.com из рабочих сайтов",
    });
    await expect(remove).toBeVisible();
    await expect(
      section.getByText("docs.example.com", { exact: true }),
    ).toBeVisible();
    await section.getByLabel("Домен рабочего сайта").fill("DOCS.EXAMPLE.COM");
    await section.getByRole("button", { name: "Добавить сайт" }).click();
    await expect(section.getByLabel("Домен рабочего сайта")).toHaveValue("");
    await expect(remove).toHaveCount(1);
    // The main settings draft predates the site addition: saving it must not remove site rules.
    await page.getByRole("button", { name: "Сохранить", exact: true }).click();
    await expect(
      page.getByText("Настройки сохранены", { exact: true }),
    ).toBeVisible();
    await page.reload();
    await expect(remove).toBeVisible();
    const stored = (await (
      await request.get("/api/v1/status")
    ).json()) as Status;
    expect(stored.tracker.settings.workSites).toContain("docs.example.com");
    expect(JSON.stringify(stored.tracker.settings.workSites)).not.toContain(
      "secret",
    );
    await section.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath("sites-desktop.png") });
    await page.setViewportSize({ width: 390, height: 844 });
    await section.scrollIntoViewIfNeeded();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    await page.screenshot({ path: info.outputPath("sites-mobile.png") });
    await section.getByLabel("Домен рабочего сайта").fill("bad domain");
    await section.getByRole("button", { name: "Добавить сайт" }).click();
    await expect(section.getByRole("alert")).toBeVisible();
    await remove.click();
    await expect(remove).toHaveCount(0);
    await page.reload();
    await expect(section.getByText("Рабочих сайтов пока нет")).toBeVisible();
  } finally {
    await request.put("/api/v1/settings", {
      headers,
      data: initial.tracker.settings,
    });
  }
});
