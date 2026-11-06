import { useRef } from 'react';
import type { PauseDuration, StatusDto } from '../api/client.ts';
import { errorMessage } from '../api/errors.ts';
import { usePauseTracking } from '../api/queries.ts';
import { formatClockTime } from '../format.ts';

const choices: { duration: PauseDuration; label: string }[] = [
  { duration: 'thirtyMinutes', label: 'For 30 minutes' },
  { duration: 'oneHour', label: 'For an hour' },
  { duration: 'untilTomorrow', label: 'Until tomorrow' },
  { duration: 'untilResumed', label: 'Until I resume' },
];

/**
 * Pausing and resuming tracking from the dashboard, with the same choices as the tray menu. While
 * paused it says until when, and offers to resume.
 */
export function TrackerControl({ status }: { status: StatusDto }) {
  const pause = usePauseTracking();
  const menu = useRef<HTMLDetailsElement>(null);

  const run = (duration: PauseDuration | null) => {
    pause.mutate(duration, {
      onSettled: () => menu.current?.removeAttribute('open'),
    });
  };

  return (
    <div className="tracker-control">
      {status.state === 'paused' ? (
        <>
          <span className="tracker-control__state">
            {status.pausedUntil ? `Paused until ${formatClockTime(status.pausedUntil)}` : 'Paused'}
          </span>
          <button
            type="button"
            className="settings__button"
            onClick={() => run(null)}
            disabled={pause.isPending}
          >
            Resume
          </button>
        </>
      ) : (
        <details ref={menu} className="tracker-control__menu">
          <summary className="settings__button">Pause</summary>
          <ul className="tracker-control__choices" aria-label="Pause tracking">
            {choices.map((c) => (
              <li key={c.duration}>
                <button type="button" onClick={() => run(c.duration)} disabled={pause.isPending}>
                  {c.label}
                </button>
              </li>
            ))}
          </ul>
        </details>
      )}
      {pause.isError && (
        <span role="alert" className="settings__errors">
          {errorMessage(pause.error)}
        </span>
      )}
    </div>
  );
}
