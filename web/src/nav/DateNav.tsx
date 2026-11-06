import { useId } from 'react';
import { addDays, weekStart } from '../dates.ts';
import { Link } from '../Link.tsx';
import { navigate, paths } from '../router.ts';
import { useArrowKeys } from './useArrowKeys.ts';

interface DateNavProps {
  /** Whether the screen shows one day or a week; the arrows step by that much. */
  view: 'day' | 'week';
  /** The date shown, `yyyy-MM-dd`; for a week, any of its dates. */
  date: string;
  /** Today's date, `yyyy-MM-dd`. Nothing after it can be tracked yet, so "next" stops there. */
  today: string;
}

/**
 * Moving between days and weeks (#35): previous and next (also ← and →), a date picker, a shortcut
 * back to today, and a switch between the day and the week. Every step is a link, so it lands in
 * the browser history and the URL always names the date on screen.
 */
export function DateNav({ view, date, today }: DateNavProps) {
  const pickerId = useId();
  const isDay = view === 'day';
  const unit = isDay ? 'day' : 'week';
  const start = isDay ? date : weekStart(date);
  const step = isDay ? 1 : 7;
  const pathFor = (d: string) => (isDay ? paths.day(d) : paths.week(d));

  const previous = pathFor(addDays(start, -step));
  const nextStart = addDays(start, step);
  const next = nextStart <= today ? pathFor(nextStart) : null;
  useArrowKeys(previous, next);

  const thisWeek = weekStart(today);
  // The day view's own address for today is `/`, so "Day" from this week opens today.
  const dayPath = (d: string) => (d === today ? paths.today() : paths.day(d));
  const isCurrent = isDay ? date === today : start === thisWeek;

  return (
    <nav className="date-nav" aria-label={`Choose a ${unit}`}>
      <div className="date-nav__steps">
        <Link
          href={previous}
          className="date-nav__button"
          aria-label={`Previous ${unit}`}
          title={`Previous ${unit} (←)`}
        >
          ←
        </Link>
        {next === null ? (
          <span
            className="date-nav__button date-nav__button--disabled"
            aria-hidden="true"
            title={`No later ${unit} yet`}
          >
            →
          </span>
        ) : (
          <Link
            href={next}
            className="date-nav__button"
            aria-label={`Next ${unit}`}
            title={`Next ${unit} (→)`}
          >
            →
          </Link>
        )}
        <Link
          href={isDay ? paths.today() : paths.week(thisWeek)}
          className="date-nav__button"
          aria-current={isCurrent ? (isDay ? 'date' : 'page') : undefined}
        >
          {isDay ? 'Today' : 'This week'}
        </Link>
      </div>

      <div className="date-nav__picker">
        <label htmlFor={pickerId} className="visually-hidden">
          Go to a date
        </label>
        <input
          id={pickerId}
          type="date"
          value={date}
          max={today}
          onChange={(event) => {
            const picked = event.target.value;
            if (picked !== '') {
              navigate(isDay ? paths.day(picked) : paths.week(weekStart(picked)));
            }
          }}
        />
      </div>

      <div className="date-nav__views">
        <Link
          href={
            isDay ? dayPath(date) : dayPath(start <= today && today < nextStart ? today : start)
          }
          className="date-nav__view"
          aria-current={isDay ? 'page' : undefined}
        >
          Day
        </Link>
        <Link
          href={paths.week(weekStart(date))}
          className="date-nav__view"
          aria-current={isDay ? undefined : 'page'}
        >
          Week
        </Link>
      </div>
    </nav>
  );
}
