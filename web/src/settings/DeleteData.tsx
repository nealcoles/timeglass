import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { errorMessage } from '../api/errors.ts';
import { useDeleteRange, useRangeSummary } from '../api/queries.ts';
import { addDays, formatDateRange, isValidDate } from '../dates.ts';
import { formatDuration } from '../format.ts';

/** The longest range whose time the confirmation can add up (the API's range limit). */
const MAX_SUMMARY_DAYS = 366;

const daysBetween = (from: string, to: string) =>
  Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000) + 1;

/**
 * Deleting the recorded time of a range of days (#36), behind a confirmation that says what will
 * be lost. Deleted time is gone for good; the database overwrites it rather than leaving it behind.
 */
export function DeleteData({ today }: { today: string }) {
  const id = useId();
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [confirming, setConfirming] = useState<{ from: string; to: string } | null>(null);
  const [deleted, setDeleted] = useState<{ from: string; to: string } | null>(null);
  const remove = useDeleteRange();

  const problem =
    !isValidDate(from) || !isValidDate(to)
      ? 'Pick the first and the last day.'
      : to < from
        ? 'The last day must not be before the first.'
        : null;

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (problem === null) {
      setDeleted(null);
      remove.reset();
      setConfirming({ from, to });
    }
  };

  const onConfirm = () => {
    if (confirming === null) {
      return;
    }
    const range = confirming;
    remove.mutate(range, {
      onSuccess: () => {
        setConfirming(null);
        setDeleted(range);
      },
    });
  };

  return (
    <section className="settings__section" aria-labelledby={`${id}-heading`}>
      <h3 id={`${id}-heading`}>Delete recorded time</h3>
      <p className="settings__hint">
        Removes everything Timeglass recorded on these days, including window titles. This can’t be
        undone.
      </p>
      <form className="delete-data" onSubmit={onSubmit} noValidate>
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
        <button type="submit" className="settings__button settings__button--danger">
          Delete…
        </button>
      </form>
      {problem !== null && (from !== '' || to !== '') && (
        <p className="settings__errors" role="alert">
          {problem}
        </p>
      )}
      {deleted !== null && (
        <p role="status" className="settings__saved">
          Deleted the time recorded {describeRange(deleted.from, deleted.to)}.
        </p>
      )}
      {confirming !== null && (
        <ConfirmDelete
          from={confirming.from}
          to={confirming.to}
          isDeleting={remove.isPending}
          error={remove.isError ? errorMessage(remove.error) : null}
          onConfirm={onConfirm}
          onCancel={() => setConfirming(null)}
        />
      )}
    </section>
  );
}

function describeRange(from: string, to: string): string {
  return from === to ? `on ${formatDateRange(from, to)}` : `from ${formatDateRange(from, to)}`;
}

interface ConfirmDeleteProps {
  from: string;
  to: string;
  isDeleting: boolean;
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * The confirmation: what will be deleted and how much active time that is. Focus starts on
 * Cancel, so Enter right after opening doesn't delete anything; Escape cancels.
 */
function ConfirmDelete({ from, to, isDeleting, error, onConfirm, onCancel }: ConfirmDeleteProps) {
  const id = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);
  const canSum = daysBetween(from, to) <= MAX_SUMMARY_DAYS;
  const summary = useRangeSummary(from, canSum ? to : addDays(from, MAX_SUMMARY_DAYS - 1), {
    enabled: canSum,
  });

  useEffect(() => cancelRef.current?.focus(), []);

  return (
    <div className="dialog-backdrop">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        aria-describedby={`${id}-body`}
        className="dialog"
        onKeyDown={(event) => {
          if (event.key === 'Escape' && !isDeleting) {
            onCancel();
          }
        }}
      >
        <h4 id={`${id}-title`}>Delete this time for good?</h4>
        <div id={`${id}-body`}>
          <p>
            Everything recorded {describeRange(from, to)} will be deleted, including window titles.
          </p>
          {canSum && summary.isSuccess && (
            <p>
              {summary.data.activeMs === 0 && summary.data.awayMs === 0
                ? 'Nothing was recorded on these days.'
                : `That is ${formatDuration(summary.data.activeMs)} of active time and ${formatDuration(summary.data.awayMs)} away.`}
            </p>
          )}
        </div>
        {error !== null && (
          <p className="settings__errors" role="alert">
            {error}
          </p>
        )}
        <div className="dialog__actions">
          <button
            ref={cancelRef}
            type="button"
            className="settings__button"
            onClick={onCancel}
            disabled={isDeleting}
          >
            Cancel
          </button>
          <button
            type="button"
            className="settings__button settings__button--danger"
            onClick={onConfirm}
            disabled={isDeleting}
          >
            {isDeleting ? 'Deleting…' : 'Delete for good'}
          </button>
        </div>
      </div>
    </div>
  );
}
