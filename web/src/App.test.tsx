import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { App } from './App.tsx';
import { server } from './test/server.ts';

describe('App', () => {
  it('shows the tracker as online when the API responds', async () => {
    server.use(http.get('/api/status', () => HttpResponse.json({ tracking: true })));

    render(<App />);

    expect(await screen.findByText('Tracker: online')).toBeInTheDocument();
  });

  it('shows the tracker as offline when the API fails', async () => {
    server.use(http.get('/api/status', () => new HttpResponse(null, { status: 500 })));

    render(<App />);

    expect(await screen.findByText('Tracker: offline')).toBeInTheDocument();
  });

  it('shows the tracker as offline on network errors', async () => {
    server.use(http.get('/api/status', () => HttpResponse.error()));

    render(<App />);

    expect(await screen.findByText('Tracker: offline')).toBeInTheDocument();
  });
});
