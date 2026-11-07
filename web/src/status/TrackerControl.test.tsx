import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { App } from '../App.tsx';
import { status } from '../test/fixtures.ts';
import { renderWithApi } from '../test/render.tsx';
import { server } from '../test/server.ts';

describe('TrackerControl', () => {
  it('pauses for an hour and resumes', async () => {
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      if (request.method === 'POST') {
        requests.push(new URL(request.url).pathname);
      }
    });
    const user = userEvent.setup();
    renderWithApi(<App />);

    await user.click(await screen.findByText('Pause'));
    await user.click(screen.getByRole('button', { name: 'For an hour' }));

    expect(await screen.findByText('Paused until 17:05')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Resume' }));

    expect(await screen.findByText('Pause')).toBeInTheDocument();
    expect(requests).toEqual(['/api/pause', '/api/resume']);
    server.events.removeAllListeners();
  });

  it('shows a pause without an end', async () => {
    server.use(
      http.get('/api/status', () =>
        HttpResponse.json(status({ state: 'paused', pausedUntil: null })),
      ),
    );

    renderWithApi(<App />);

    expect(await screen.findByText('Paused')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resume' })).toBeInTheDocument();
  });

  it('says when pausing failed', async () => {
    server.use(http.post('/api/pause', () => new HttpResponse(null, { status: 500 })));
    const user = userEvent.setup();
    renderWithApi(<App />);

    await user.click(await screen.findByText('Pause'));
    await user.click(screen.getByRole('button', { name: 'Until I resume' }));

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/Timeglass ran into a problem/),
    );
  });
});
