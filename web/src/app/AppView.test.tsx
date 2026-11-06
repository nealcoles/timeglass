import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { AppDayDto } from '../api/client.ts';
import { appDay, apps, demoDate } from '../test/fixtures.ts';
import { renderWithApi } from '../test/render.tsx';
import { server } from '../test/server.ts';
import { AppView } from './AppView.tsx';

function useAppDay(day: AppDayDto) {
  server.use(http.get('/api/days/:date/apps/:appKey', () => HttpResponse.json(day)));
}

/** Each row of the title list as [title, time, share]. */
function rows() {
  return within(screen.getByRole('table'))
    .getAllByRole('row')
    .slice(1)
    .map((row) => [
      within(row).getByRole('rowheader').textContent,
      ...within(row)
        .getAllByRole('cell')
        .map((c) => c.textContent),
    ]);
}

describe('AppView', () => {
  it('shows a loading state, then the app and its total', async () => {
    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading the titles…');
    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Visual Studio Code');
    expect(screen.getByText('Active').nextElementSibling).toHaveTextContent('3h');
  });

  it('lists titles longest first with time and share of the app, then the rest', async () => {
    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    await screen.findByRole('table', {
      name: 'Time in Visual Studio Code per window title, longest first',
    });
    expect(rows()).toEqual([
      ['TrackingEngine.cs - timeglass - Visual Studio Code', '1h 30m', '50%'],
      ['DaySummaries.cs - timeglass - Visual Studio Code', '1h', '33%'],
      ['3 other titles', '30m', '17%'],
    ]);
  });

  it('gives a long title its full text as a tooltip', async () => {
    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    const title = await screen.findByText('TrackingEngine.cs - timeglass - Visual Studio Code');
    expect(title).toHaveAttribute('title', 'TrackingEngine.cs - timeglass - Visual Studio Code');
  });

  it('filters the titles as you type', async () => {
    const user = userEvent.setup();
    renderWithApi(<AppView date={demoDate} appKey="vscode" />);
    await screen.findByRole('table');

    await user.type(screen.getByRole('searchbox', { name: 'Filter titles' }), 'daysumm');

    expect(rows()).toEqual([['DaySummaries.cs - timeglass - Visual Studio Code', '1h', '33%']]);
    expect(screen.getByText(/The search covers the 2 titles with the most time/)).toHaveTextContent(
      "the 3 shorter ones (30m) aren't listed.",
    );
  });

  it('says so when no title matches, and shows all again when cleared', async () => {
    const user = userEvent.setup();
    renderWithApi(<AppView date={demoDate} appKey="vscode" />);
    await screen.findByRole('table');
    const search = screen.getByRole('searchbox', { name: 'Filter titles' });

    await user.type(search, 'inbox');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByText('No titles match “inbox”.')).toBeInTheDocument();

    await user.clear(search);
    expect(rows()).toHaveLength(3);
  });

  it('shows time without a title as its own row', async () => {
    const base = appDay();
    useAppDay({
      ...base,
      titles: [...base.titles, { title: null, durationMs: 5 * 60_000, share: 5 / 180, percent: 3 }],
    });

    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    await screen.findByRole('table');
    expect(rows()).toContainEqual(['No title recorded', '5m', '3%']);
  });

  it('explains when no titles were recorded for the app', async () => {
    useAppDay({
      ...appDay(),
      titles: [{ title: null, durationMs: 180 * 60_000, share: 1, percent: 100 }],
      other: null,
    });

    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    expect(
      await screen.findByText(/No window titles were recorded for Visual Studio Code/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.getByText('Active').nextElementSibling).toHaveTextContent('3h');
  });

  it('says so when the app had no time that day', async () => {
    renderWithApi(<AppView date={demoDate} appKey={apps.outlook.key} />);

    expect(await screen.findByText('No time in Outlook on this day.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('words the empty state and the way back for today', async () => {
    renderWithApi(<AppView date={demoDate} appKey={apps.outlook.key} isToday />);

    expect(await screen.findByText('No time in Outlook yet today.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Back to today/ })).toHaveAttribute('href', '/');
  });

  it('links back to its day', async () => {
    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    expect(screen.getByRole('link', { name: /Back to the day/ })).toHaveAttribute(
      'href',
      `/day/${demoDate}`,
    );
    await screen.findByRole('table');
  });

  it('shows an error when the request fails', async () => {
    server.use(
      http.get('/api/days/:date/apps/:appKey', () => new HttpResponse(null, { status: 500 })),
    );

    renderWithApi(<AppView date={demoDate} appKey="vscode" />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Timeglass ran into a problem. Try again in a moment.',
    );
  });
});
