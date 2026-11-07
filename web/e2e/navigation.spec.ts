import { expect, test } from '@playwright/test';
import { SUNDAY, THIS_MONDAY, TODAY, YESTERDAY } from './demo.ts';

const heading = (page: import('@playwright/test').Page) => page.getByRole('heading', { level: 2 });

test('moves between days with the buttons, the arrow keys and the date picker', async ({
  page,
}) => {
  await page.goto('/');
  await expect(heading(page)).toHaveText('Today, Thursday, 5 November 2026');
  // Nothing after today can be tracked yet.
  await expect(page.getByRole('link', { name: 'Next day' })).toHaveCount(0);

  await page.getByRole('link', { name: 'Previous day' }).click();
  await expect(page).toHaveURL(`/day/${YESTERDAY}`);
  await expect(heading(page)).toHaveText('Wednesday, 4 November 2026');
  await expect(page.getByRole('heading', { name: 'Where the time went' })).toBeVisible();

  await page.keyboard.press('ArrowRight');
  await expect(page).toHaveURL(`/day/${TODAY}`);
  await expect(heading(page)).toHaveText('Today, Thursday, 5 November 2026');

  await page.getByLabel('Go to a date').fill(SUNDAY);
  await expect(page).toHaveURL(`/day/${SUNDAY}`);
  await expect(page.getByText('Nothing was tracked on this day.')).toBeVisible();

  await page.goBack();
  await expect(page).toHaveURL(`/day/${TODAY}`);
});

test('switches to the week and back to a day', async ({ page }) => {
  await page.goto(`/day/${YESTERDAY}`);

  await page.getByRole('link', { name: 'Week', exact: true }).click();
  await expect(page).toHaveURL(`/week/${THIS_MONDAY}`);
  await expect(page.getByRole('link', { name: 'Next week' })).toHaveCount(0);

  await page.getByRole('link', { name: 'Day', exact: true }).click();
  await expect(page).toHaveURL('/');
  await expect(heading(page)).toHaveText('Today, Thursday, 5 November 2026');
});

test('a deep link opens the page it names', async ({ page }) => {
  await page.goto(`/day/${YESTERDAY}`);
  await expect(heading(page)).toHaveText('Wednesday, 4 November 2026');

  await page.goto('/no/such/page');
  await expect(heading(page)).toHaveText('Page not found');
});
