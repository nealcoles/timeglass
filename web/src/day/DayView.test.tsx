import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import userEvent from '@testing-library/user-event';
import type { DaySummaryDto } from '../api/client.ts';
import { daySummary, demoDate } from '../test/fixtures.ts';
import { renderWithApi } from '../test/render.tsx';
import { server } from '../test/server.ts';
import { DayView } from './DayView.tsx';

function useSummary(summary: DaySummaryDto) {
  server.use(http.get('/api/days/:date/summary', () => HttpResponse.json(summary)));
}

function totals() {
  return Object.fromEntries(
    screen
      .getAllByRole('term')
      .map((dt) => [dt.textContent, dt.nextElementSibling?.textContent ?? '']),
  );
}

describe('DayView', () => {
  it('switches to the grouped totals without changing the application timeline', async () => {
    server.use(
      http.get('/api/range/groups', () =>
        HttpResponse.json({
          from: demoDate,
          to: demoDate,
          start: '',
          end: '',
          activeMs: 60000,
          awayMs: 0,
          days: [],
          apps: [
            {
              app: { key: 'group-0', name: 'Investigator' },
              durationMs: 60000,
              share: 1,
              percent: 100,
            },
          ],
        }),
      ),
    );
    const user = userEvent.setup();
    renderWithApi(<DayView date={demoDate} />);
    await screen.findByRole('table');
    await user.click(screen.getByRole('button', { name: 'Keyword group' }));
    expect(screen.getByRole('button', { name: 'Keyword group' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    const table = await screen.findByRole('table', {
      name: 'Tracked time per group, longest first',
    });

    expect(within(table).getByText('Investigator')).toBeInTheDocument();
    expect(screen.getByText('The timeline is coloured by application.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Application' }));
    expect(screen.getByRole('button', { name: 'Application' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(
      screen.getByRole('table', { name: 'Active time per app, longest first' }),
    ).toBeInTheDocument();
  });

  it('lets an away-only day be viewed as a group without an application link', async () => {
    useSummary({
      ...daySummary(),
      activeMs: 0,
      awayMs: 60000,
      firstActivity: null,
      lastActivity: null,
      workdayMs: 0,
      apps: [],
    });
    server.use(
      http.get('/api/range/groups', () =>
        HttpResponse.json({
          from: demoDate,
          to: demoDate,
          start: '',
          end: '',
          activeMs: 0,
          awayMs: 60000,
          days: [],
          apps: [{ app: { key: 'away', name: 'Away' }, durationMs: 60000, share: 1, percent: 100 }],
        }),
      ),
    );
    const user = userEvent.setup();
    renderWithApi(<DayView date={demoDate} />);
    expect(await screen.findByText(/No active time was tracked/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Keyword group' }));
    const table = await screen.findByRole('table', {
      name: 'Tracked time per group, longest first',
    });
    expect(table).toHaveTextContent('Away');
    expect(table).toHaveTextContent('100%');
    expect(within(table).queryByRole('link', { name: 'Away' })).not.toBeInTheDocument();
  });
  it('shows a loading state until the summary arrives', async () => {
    renderWithApi(<DayView date={demoDate} />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading the day…');
    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(screen.queryByText('Loading the day…')).not.toBeInTheDocument();
  });

  it('shows the totals and the first and last activity', async () => {
    renderWithApi(<DayView date={demoDate} />);

    await screen.findByRole('table');
    expect(totals()).toEqual({
      Active: '7h',
      Away: '1h 5m',
      'First activity': '08:12',
      'Last activity': '16:05',
      Workday: '7h 5m',
    });
  });

  it('leaves out the workday when there was no activity (#110)', async () => {
    useSummary({
      ...daySummary(),
      activeMs: 0,
      firstActivity: null,
      lastActivity: null,
      workdayMs: 0,
    });

    renderWithApi(<DayView date={demoDate} />);

    await screen.findByRole('table');
    expect(totals()).not.toHaveProperty('Workday');
  });

  it('heads the view with the date, and "Today" for today', async () => {
    renderWithApi(<DayView date={demoDate} today={demoDate} />);

    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent(/^Today, .*2026/);
    await screen.findByRole('table');
  });

  it('lists apps longest first with time and percentage', async () => {
    renderWithApi(<DayView date={demoDate} />);

    const table = await screen.findByRole('table', { name: 'Active time per app, longest first' });
    const rows = within(table)
      .getAllByRole('row')
      .slice(1)
      .map((row) =>
        within(row)
          .getAllByRole('cell')
          .map((cell) => cell.textContent),
      );
    const names = within(table)
      .getAllByRole('rowheader')
      .map((cell) => cell.textContent);

    expect(names).toEqual([
      'Visual Studio Code',
      'Outlook',
      'Microsoft Edge',
      'Microsoft Teams',
      'Windows Terminal',
    ]);
    expect(rows).toEqual([
      ['3h', '43%'],
      ['1h 45m', '25%'],
      ['1h 15m', '18%'],
      ['45m', '11%'],
      ['15m', '3%'],
    ]);
  });

  it('shows when the time went, in the app list colours', async () => {
    renderWithApi(<DayView date={demoDate} />);

    expect(await screen.findByRole('heading', { name: 'When' })).toBeInTheDocument();
    const lane = await screen.findByRole('group', { name: /Timeline of the day/ });
    const table = screen.getByRole('table');
    const swatch = table.querySelector<HTMLElement>('tr[data-app="vscode"] .app-shares__swatch');
    const block = lane.querySelector<HTMLElement>('[data-app="vscode"]');
    expect(block?.style.backgroundColor).toBeTruthy();
    expect(block?.style.backgroundColor).toBe(swatch?.style.backgroundColor);
  });

  it('lists every app by name instead of folding some into Other', async () => {
    const base = daySummary();
    const extra = ['a', 'b', 'c'].map((key) => ({
      app: { key, name: `App ${key}` },
      durationMs: 60_000,
      share: 0.01,
      percent: 1,
    }));
    useSummary({ ...base, apps: [...base.apps, ...extra] });

    renderWithApi(<DayView date={demoDate} />);

    const table = await screen.findByRole('table');
    expect(within(table).queryByRole('rowheader', { name: /^Other/ })).toBeNull();
    for (const key of ['a', 'b', 'c']) {
      expect(within(table).getByRole('link', { name: `App ${key}` })).toBeInTheDocument();
    }
    expect(within(table).getAllByRole('row')).toHaveLength(1 + base.apps.length + extra.length);
  });

  it('gives each app a stable colour', async () => {
    renderWithApi(<DayView date={demoDate} />);

    const table = await screen.findByRole('table');
    const swatch = (key: string) =>
      table.querySelector<HTMLElement>(`tr[data-app="${key}"] .app-shares__swatch`)?.style
        .backgroundColor;
    expect(swatch('vscode')).toBeTruthy();
    expect(swatch('vscode')).not.toBe(swatch('outlook'));
  });

  it('shows an empty state for a day without activity', async () => {
    useSummary({
      ...daySummary(),
      activeMs: 0,
      awayMs: 0,
      firstActivity: null,
      lastActivity: null,
      apps: [],
    });

    renderWithApi(<DayView date={demoDate} />);

    expect(await screen.findByText('Nothing was tracked on this day.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('words the empty state for today', async () => {
    useSummary({
      ...daySummary(),
      activeMs: 0,
      awayMs: 0,
      firstActivity: null,
      lastActivity: null,
      apps: [],
    });

    renderWithApi(<DayView date={demoDate} today={demoDate} />);

    expect(await screen.findByText(/Nothing tracked yet today/)).toBeInTheDocument();
  });

  it('shows an error when the summary fails', async () => {
    server.use(http.get('/api/days/:date/summary', () => new HttpResponse(null, { status: 500 })));

    renderWithApi(<DayView date={demoDate} />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Timeglass ran into a problem. Try again in a moment.',
    );
  });
});
