import { expect, type APIRequestContext } from '@playwright/test';
import type { components } from '../src/api/schema';

/**
 * The moment the dev host's clock is stopped at: Thursday, 5 November 2026, 14:00 in Oslo. The demo
 * data then holds today until 14:00 and the five workdays before it; the weekend is empty.
 */
export const NOW = '2026-11-05T13:00:00Z';
export const TIME_ZONE = 'Europe/Oslo';
export const TODAY = '2026-11-05';
export const YESTERDAY = '2026-11-04';
export const SUNDAY = '2026-11-01';
export const THIS_MONDAY = '2026-11-02';

type DaySummary = components['schemas']['DaySummaryDto'];

/** A day's summary straight from the API, to check the page against. */
export async function daySummary(request: APIRequestContext, date: string): Promise<DaySummary> {
  const response = await request.get(`/api/days/${date}/summary`);
  expect(response.ok()).toBe(true);
  return (await response.json()) as DaySummary;
}
