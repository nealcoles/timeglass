import { expect, test } from '@playwright/test';
import { TODAY } from './demo.ts';

test("drills into an app's window titles and filters them", async ({ page, request }) => {
  const response = await request.get(`/api/days/${TODAY}/apps/vscode`);
  expect(response.ok()).toBe(true);
  const vscode = (await response.json()) as { titles: { title: string | null }[] };
  const first = vscode.titles.find((t) => t.title !== null)?.title;
  expect(first).toBeTruthy();

  await page.goto('/');
  await page.getByRole('link', { name: 'Visual Studio Code' }).click();

  await expect(page).toHaveURL(`/day/${TODAY}/app/vscode`);
  await expect(page.getByRole('heading', { level: 2 })).toHaveText('Visual Studio Code');
  const titles = page.getByRole('table', { name: /per window title/ }).locator('tbody tr');
  await expect(titles.first()).toBeVisible();
  await expect(titles).toHaveCount(vscode.titles.length);

  await page.getByLabel('Filter titles').fill('README');
  await expect(titles).toHaveCount(1);
  await expect(titles.first()).toContainText('README.md - timeglass - Visual Studio Code');

  await page.getByLabel('Filter titles').fill('no title like this');
  await expect(page.getByText('No titles match “no title like this”.')).toBeVisible();

  await page.getByRole('link', { name: '← Back to today' }).click();
  await expect(page).toHaveURL('/');
});
