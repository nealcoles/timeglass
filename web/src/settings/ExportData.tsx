import { useId, useState } from 'react';
import { addDays, isValidDate } from '../dates.ts';
import { exportUrl, type ExportFormat, type ExportGranularity } from './exportUrl.ts';

/**
 * Getting the recorded time out as a file (#39), for a spreadsheet, a script or a timesheet. The
 * button is a plain link to the API, so the browser saves the answer as it arrives instead of the
 * page holding it all in memory first.
 */
export function ExportData({ today }: { today: string }) {
  const id = useId();
  const [from, setFrom] = useState(() => addDays(today, -6));
  const [to, setTo] = useState(today);
  const [granularity, setGranularity] = useState<ExportGranularity>('intervals');
  const [format, setFormat] = useState<ExportFormat>('csv');

  const problem =
    !isValidDate(from) || !isValidDate(to)
      ? 'Pick the first and the last day.'
      : to < from
        ? 'The last day must not be before the first.'
        : null;

  return (
    <section className="settings__section" aria-labelledby={`${id}-heading`}>
      <h3 id={`${id}-heading`}>Export recorded time</h3>
      <p className="settings__hint">
        Saves the time recorded on these days as a file. Apps you have excluded are left out, and so
        are window titles you have turned off.
      </p>
      <div className="export-data">
        <label>
          From
          <input
            type="date"
            value={from}
            max={today}
            onChange={(event) => setFrom(event.target.value)}
          />
        </label>
        <label>
          To
          <input
            type="date"
            value={to}
            max={today}
            onChange={(event) => setTo(event.target.value)}
          />
        </label>
        <label>
          Rows
          <select
            value={granularity}
            onChange={(event) => setGranularity(event.target.value as ExportGranularity)}
          >
            <option value="intervals">Every interval, with window titles</option>
            <option value="daily-apps">Total per day and app</option>
          </select>
        </label>
        <label>
          Format
          <select
            value={format}
            onChange={(event) => setFormat(event.target.value as ExportFormat)}
          >
            <option value="csv">CSV (spreadsheet)</option>
            <option value="json">JSON</option>
          </select>
        </label>
        {problem === null ? (
          <a className="settings__button" href={exportUrl(from, to, format, granularity)} download>
            Export
          </a>
        ) : (
          <button type="button" className="settings__button" disabled>
            Export
          </button>
        )}
      </div>
      {problem !== null && (from !== '' || to !== '') && (
        <p className="settings__errors" role="alert">
          {problem}
        </p>
      )}
    </section>
  );
}
