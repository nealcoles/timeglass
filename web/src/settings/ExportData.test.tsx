import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { demoDate } from '../test/fixtures.ts';
import { renderWithApi } from '../test/render.tsx';
import { ExportData } from './ExportData.tsx';
import { exportUrl } from './exportUrl.ts';

async function setRange(user: ReturnType<typeof userEvent.setup>, from: string, to: string) {
  const fromField = screen.getByLabelText('From');
  const toField = screen.getByLabelText('To');
  await user.clear(fromField);
  if (from !== '') {
    await user.type(fromField, from);
  }
  await user.clear(toField);
  if (to !== '') {
    await user.type(toField, to);
  }
}

describe('ExportData', () => {
  it('links to a CSV of the last seven days by default', () => {
    renderWithApi(<ExportData today={demoDate} />);

    expect(screen.getByLabelText('From')).toHaveValue('2026-10-30');
    expect(screen.getByLabelText('To')).toHaveValue(demoDate);
    const link = screen.getByRole('link', { name: 'Export' });
    expect(link).toHaveAttribute(
      'href',
      `/api/export?from=2026-10-30&to=${demoDate}&format=csv&granularity=intervals`,
    );
    expect(link).toHaveAttribute('download');
  });

  it('follows the range, the rows and the format picked', async () => {
    const user = userEvent.setup();
    renderWithApi(<ExportData today={demoDate} />);

    await setRange(user, '2026-11-02', '2026-11-04');
    await user.selectOptions(screen.getByLabelText('Rows'), 'Total per day and app');
    await user.selectOptions(screen.getByLabelText('Format'), 'JSON');

    expect(screen.getByRole('link', { name: 'Export' })).toHaveAttribute(
      'href',
      '/api/export?from=2026-11-02&to=2026-11-04&format=json&granularity=daily-apps',
    );
  });

  it('offers no export for a range that ends before it starts', async () => {
    const user = userEvent.setup();
    renderWithApi(<ExportData today={demoDate} />);

    await setRange(user, '2026-11-04', '2026-11-02');

    expect(screen.getByRole('alert')).toHaveTextContent(
      'The last day must not be before the first.',
    );
    expect(screen.queryByRole('link', { name: 'Export' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled();
  });

  it('asks for both days when one is missing', async () => {
    const user = userEvent.setup();
    renderWithApi(<ExportData today={demoDate} />);

    await setRange(user, '2026-11-04', '');

    expect(screen.getByRole('alert')).toHaveTextContent('Pick the first and the last day.');
    expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled();
  });

  it('builds the address the API expects', () => {
    expect(exportUrl('2026-01-01', '2026-12-31', 'csv', 'daily-apps')).toBe(
      '/api/export?from=2026-01-01&to=2026-12-31&format=csv&granularity=daily-apps',
    );
  });
});
