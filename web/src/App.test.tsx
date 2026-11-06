import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { App } from './App.tsx';
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
