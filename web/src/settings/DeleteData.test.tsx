import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { formatDateRange } from '../dates.ts';
import { demoDate } from '../test/fixtures.ts';
import { renderWithApi } from '../test/render.tsx';
import { server } from '../test/server.ts';
import { DeleteData } from './DeleteData.tsx';

/** A range as the page shows it; text matching collapses the thin spaces around the dash. */
const range = (from: string, to: string) => formatDateRange(from, to).replace(/\s+/g, ' ');

/** Records the DELETE requests as `from..to (confirm)`. */
function captureDeletes(status = 204) {
  const deletes: string[] = [];
  server.use(
    http.delete('/api/intervals', async ({ request }) => {
      const url = new URL(request.url);
      const { confirm } = (await request.json()) as { confirm: string };
      deletes.push(`${url.searchParams.get('from')}..${url.searchParams.get('to')} (${confirm})`);
      return new HttpResponse(null, { status });
    }),
  );
  return deletes;
}

async function setRange(user: ReturnType<typeof userEvent.setup>, from: string, to: string) {
  const fromField = screen.getByLabelText('From');
  const toField = screen.getByLabelText('To');
  await user.clear(fromField);
  await user.type(fromField, from);
  await user.clear(toField);
  await user.type(toField, to);
}

describe('DeleteData', () => {
  it('asks for confirmation, says how much time is deleted, and deletes', async () => {
    const deletes = captureDeletes();
    const user = userEvent.setup();
    renderWithApi(<DeleteData today={demoDate} />);

    await setRange(user, '2026-11-02', '2026-11-04');
    await user.click(screen.getByRole('button', { name: 'Delete…' }));

    const dialog = await screen.findByRole('alertdialog', { name: 'Delete this time for good?' });
    expect(dialog).toHaveTextContent(`from ${range('2026-11-02', '2026-11-04')}`);
    expect(await within(dialog).findByText(/of active time and/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    expect(deletes).toEqual([]);

    await user.click(within(dialog).getByRole('button', { name: 'Delete for good' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      `Deleted the time recorded from ${range('2026-11-02', '2026-11-04')}.`,
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(deletes).toEqual(['2026-11-02..2026-11-04 (2026-11-02..2026-11-04)']);
  });

  it('deletes a single day, today by default', async () => {
    const deletes = captureDeletes();
    const user = userEvent.setup();
    renderWithApi(<DeleteData today={demoDate} />);

    await user.click(screen.getByRole('button', { name: 'Delete…' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent(`on ${range(demoDate, demoDate)}`);
    await user.click(within(dialog).getByRole('button', { name: 'Delete for good' }));

    await waitFor(() =>
      expect(deletes).toEqual([`${demoDate}..${demoDate} (${demoDate}..${demoDate})`]),
    );
  });

  it('cancels with the button or Escape without deleting', async () => {
    const deletes = captureDeletes();
    const user = userEvent.setup();
    renderWithApi(<DeleteData today={demoDate} />);

    await user.click(screen.getByRole('button', { name: 'Delete…' }));
    await user.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Delete…' }));
    await screen.findByRole('alertdialog');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    expect(deletes).toEqual([]);
  });

  it('will not open for a range that ends before it starts', async () => {
    const user = userEvent.setup();
    renderWithApi(<DeleteData today={demoDate} />);

    await setRange(user, '2026-11-04', '2026-11-02');
    expect(screen.getByRole('alert')).toHaveTextContent(
      'The last day must not be before the first.',
    );
    await user.click(screen.getByRole('button', { name: 'Delete…' }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('keeps the dialog open and says why when deleting fails', async () => {
    captureDeletes(500);
    const user = userEvent.setup();
    renderWithApi(<DeleteData today={demoDate} />);

    await user.click(screen.getByRole('button', { name: 'Delete…' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete for good' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      /Timeglass ran into a problem/,
    );
  });

  it('does not add up a range longer than a year', async () => {
    const user = userEvent.setup();
    renderWithApi(<DeleteData today={demoDate} />);

    await setRange(user, '2024-01-01', '2026-11-05');
    await user.click(screen.getByRole('button', { name: 'Delete…' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(dialog).toHaveTextContent(`from ${range('2024-01-01', '2026-11-05')}`);
    expect(dialog).not.toHaveTextContent(/active time/);
  });
});
