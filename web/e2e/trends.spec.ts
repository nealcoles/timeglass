import { expect, test } from '@playwright/test';

test('opens the trends from the header and narrows them to one app', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('link', { name: 'Trends', exact: true }).click();
  await expect(page).toHaveURL('/trends');
  await expect(page.getByRole('heading', { level: 2 })).toHaveText('Trends');

  const table = page.getByRole('table', { name: /The apps in the chart/ });
  const lines = page.locator('.trend-chart__series');
  await expect(lines).toHaveCount(5);
  await expect(table.getByRole('row')).toHaveCount(6);

  await page.getByLabel('Show').selectOption({ label: 'Top 3 apps' });
  await expect(lines).toHaveCount(3);

  const app = page.getByLabel('App');
  const firstApp = (await app.locator('option').nth(1).textContent()) ?? '';
  await app.selectOption({ label: firstApp });
  await expect(lines).toHaveCount(1);
  await expect(table.getByRole('rowheader')).toHaveText([firstApp]);
  await expect(page.getByLabel('Show')).toBeDisabled();

  await page.getByLabel('Period').selectOption({ label: 'Last 365 days' });
  await expect(page.getByRole('img', { name: /week by week/ })).toBeVisible();
});
