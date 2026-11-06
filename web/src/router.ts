import { useSyncExternalStore, type MouseEvent } from 'react';
import { isValidDate } from './dates.ts';

/**
 * The dashboard's routes. The app serves `index.html` for any path that isn't a file (SPA
 * fallback), so these work as deep links and on reload. The dashboard has few enough screens that
 * a small history-API router does the job without a routing library.
 */
export type Route =
  | { name: 'today' }
  | { name: 'day'; date: string }
  | { name: 'app'; date: string; appKey: string }
  | { name: 'thisWeek' }
  | { name: 'week'; date: string }
  | { name: 'settings' }
  | { name: 'notFound' };

/**
 * The route for a path such as `/day/2026-11-05/app/outlook`. A week is named by any of its dates,
 * `/week/2026-11-05`; the links the dashboard makes use its Monday.
 */
export function parseRoute(pathname: string): Route {
  const parts = pathname.split('/').filter((p) => p !== '');
  if (parts.length === 0) {
    return { name: 'today' };
  }
  if (parts[0] === 'settings' && parts.length === 1) {
    return { name: 'settings' };
  }
  if (parts[0] === 'week' && parts.length === 1) {
    return { name: 'thisWeek' };
  }
  if (
    parts[0] === 'week' &&
    parts.length === 2 &&
    parts[1] !== undefined &&
    isValidDate(parts[1])
  ) {
    return { name: 'week', date: parts[1] };
  }
  if (parts[0] !== 'day' || parts[1] === undefined || !isValidDate(parts[1])) {
    return { name: 'notFound' };
  }
  const date = parts[1];
  if (parts.length === 2) {
    return { name: 'day', date };
  }
  if (parts.length === 4 && parts[2] === 'app' && parts[3] !== undefined) {
    try {
      return { name: 'app', date, appKey: decodeURIComponent(parts[3]) };
    } catch {
      return { name: 'notFound' };
    }
  }
  return { name: 'notFound' };
}

export const paths = {
  today: () => '/',
  day: (date: string) => `/day/${date}`,
  app: (date: string, appKey: string) => `/day/${date}/app/${encodeURIComponent(appKey)}`,
  week: (date: string) => `/week/${date}`,
  settings: () => '/settings',
};

const listeners = new Set<() => void>();

function subscribe(listener: () => void) {
  listeners.add(listener);
  globalThis.addEventListener('popstate', listener);
  return () => {
    listeners.delete(listener);
    globalThis.removeEventListener('popstate', listener);
  };
}

const getPathname = () => globalThis.location.pathname;

/** Goes to `path` without reloading the page, and adds it to the browser history. */
export function navigate(path: string) {
  if (path === getPathname()) {
    return;
  }
  globalThis.history.pushState(null, '', path);
  globalThis.scrollTo?.(0, 0);
  for (const listener of listeners) {
    listener();
  }
}

/** The current route; the component re-renders when it changes. */
export function useRoute(): Route {
  return parseRoute(useSyncExternalStore(subscribe, getPathname));
}

/**
 * A click handler for an `<a href>` inside the dashboard: a plain click navigates in place, while
 * a modified click (new tab, new window) is left to the browser.
 */
export function followLink(event: MouseEvent<HTMLAnchorElement>) {
  if (
    event.defaultPrevented ||
    event.button !== 0 ||
    event.metaKey ||
    event.ctrlKey ||
    event.shiftKey ||
    event.altKey
  ) {
    return;
  }
  event.preventDefault();
  navigate(event.currentTarget.getAttribute('href') ?? '/');
}
