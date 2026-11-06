import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { App } from './App.tsx';
import { demoDate } from './test/fixtures.ts';
import { unauthorized } from './test/handlers.ts';
import { renderWithApi } from './test/render.tsx';
import { server } from './test/server.ts';

describe('App', () => {
  it('shows the tracker as online when the API responds', async () => {
    renderWithApi(<App />);

    expect(await screen.findByText('Tracker: online')).toBeInTheDocument();
  });

  it('shows the tracker as offline when the API fails', async () => {
    server.use(http.get('/api/status', () => new HttpResponse(null, { status: 500 })));

    renderWithApi(<App />);

    expect(await screen.findByText('Tracker: offline')).toBeInTheDocument();
  });

  it('shows the tracker as offline on network errors', async () => {
    server.use(http.get('/api/status', () => HttpResponse.error()));

    renderWithApi(<App />);

    expect(await screen.findByText('Tracker: offline')).toBeInTheDocument();
  });

  it('asks to open the dashboard from the tray icon when not signed in', async () => {
    server.use(http.get('/api/status', unauthorized));

    renderWithApi(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Open the dashboard from the Timeglass tray icon to sign in.',
    );
    expect(screen.queryByText(/Tracker:/)).not.toBeInTheDocument();
  });
});

describe('App routes', () => {
  it('opens an app from the day view and goes back', async () => {
    const user = userEvent.setup();
    renderWithApi(<App />);

    const table = await screen.findByRole('table');
    await user.click(within(table).getByRole('link', { name: 'Visual Studio Code' }));

    expect(globalThis.location.pathname).toBe(`/day/${demoDate}/app/vscode`);
    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(
      'Visual Studio Code',
    );
    expect(await screen.findByText(/TrackingEngine\.cs/)).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: /Back to today/ }));
    expect(globalThis.location.pathname).toBe('/');
    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^Today, /);
  });

  it('opens a deep link to an app on another day', async () => {
    globalThis.history.replaceState(null, '', '/day/2026-11-04/app/vscode');

    renderWithApi(<App />);

    expect(await screen.findByRole('link', { name: /Back to the day/ })).toHaveAttribute(
      'href',
      '/day/2026-11-04',
    );
    expect(await screen.findByRole('table')).toBeInTheDocument();
  });

  it('opens a deep link to another day', async () => {
    globalThis.history.replaceState(null, '', '/day/2026-11-04');

    renderWithApi(<App />);

    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(
      /^Wednesday, .*2026/,
    );
  });

  it('shows a way home for an unknown page', async () => {
    globalThis.history.replaceState(null, '', '/nowhere');

    renderWithApi(<App />);

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to today' })).toHaveAttribute('href', '/');
  });

  it('steps back a day with the arrow key and returns with the back button', async () => {
    const user = userEvent.setup();
    renderWithApi(<App />);

    await screen.findByRole('table');
    await user.keyboard('{ArrowLeft}');
    expect(globalThis.location.pathname).toBe('/day/2026-11-04');
    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^Wednesday, /);

    act(() => {
      globalThis.history.back();
    });
    await waitFor(() => expect(globalThis.location.pathname).toBe('/'));
    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^Today, /);
  });

  it('opens the week and a day from it', async () => {
    const user = userEvent.setup();
    renderWithApi(<App />);

    await screen.findByRole('table');
    await user.click(screen.getByRole('link', { name: 'Week' }));
    expect(globalThis.location.pathname).toBe('/week/2026-11-02');
    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^This week, /);

    const days = await screen.findByRole('table', { name: /Active time per day/ });
    await user.click(within(days).getByRole('link', { name: /Tue/ }));
    expect(globalThis.location.pathname).toBe('/day/2026-11-03');
    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^Tuesday, /);
  });

  it('opens this week at /week', async () => {
    globalThis.history.replaceState(null, '', '/week');

    renderWithApi(<App />);

    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^This week, /);
  });

  it('opens a deep link to another week', async () => {
    globalThis.history.replaceState(null, '', '/week/2026-10-28');

    renderWithApi(<App />);

    expect(await screen.findByRole('heading', { level: 2 })).toHaveTextContent(/^Week of /);
    expect(screen.getByRole('link', { name: 'Next week' })).toHaveAttribute(
      'href',
      '/week/2026-11-02',
    );
  });
});
