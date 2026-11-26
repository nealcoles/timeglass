import { useSyncExternalStore, type MouseEvent } from 'react';
import { isValidDate } from './dates.ts';
import type { GroupingView } from './groups/ViewSelector.tsx';

/**
 * The dashboard's routes. The app serves `index.html` for any path that isn't a file (SPA
 * fallback), so these work as deep links and on reload. The dashboard has few enough screens that
 * a small history-API router does the job without a routing library.
 */
export type Route =
  | { name: 'today'; view?: 'groups' }
  | { name: 'day'; date: string; view?: 'groups' }
  | { name: 'app'; date: string; appKey: string }
  | { name: 'thisWeek'; view?: 'groups' }
  | { name: 'week'; date: string; view?: 'groups' }
  | { name: 'trends'; view?: 'groups'; period?: number }
  | { name: 'group'; from: string; to: string; groupName: string; source: GroupSource }
  | {
      name: 'groupApp';
      from: string;
      to: string;
      groupName: string;
      source: GroupSource;
      appKey: string;
    }
  | { name: 'settings' }
  | { name: 'notFound' };

export type GroupSource = 'day' | 'week' | 'trends';

/** Preserve the grouping selection when moving between day and week views. */
export function withGroupingView(path: string, view: GroupingView): string {
  return view === 'groups' ? `${path}?view=groups` : path;
}

/**
 * The route for a path such as `/day/2026-11-05/app/outlook`. A week is named by any of its dates,
 * `/week/2026-11-05`; the links the dashboard makes use its Monday.
 */
export function parseRoute(pathname: string): Route {
  const [path = '', search = ''] = pathname.split('?', 2);
  const params = new URLSearchParams(search);
  const view = params.get('view') === 'groups' ? { view: 'groups' as const } : {};
  const parts = path.split('/').filter((p) => p !== '');
  if (parts.length === 0) {
    return { name: 'today', ...view };
  }
  if (parts[0] === 'settings' && parts.length === 1) {
    return { name: 'settings' };
  }
  if (parts[0] === 'trends' && parts.length === 1) {
    const period = Number(params.get('period'));
    return {
      name: 'trends',
      ...view,
      ...([7, 30, 90, 180, 365].includes(period) ? { period } : {}),
    };
  }
  if (
    parts[0] === 'group' &&
    (parts.length === 3 || (parts.length === 4 && parts[3] === 'app')) &&
    parts[1] &&
    parts[2] &&
    isValidDate(parts[1]) &&
    isValidDate(parts[2]) &&
    parts[1] <= parts[2]
  ) {
    const groupName = params.get('name');
    const source = params.get('source');
    if (groupName && (source === 'day' || source === 'week' || source === 'trends')) {
      if (parts.length === 3) {
        return { name: 'group', from: parts[1], to: parts[2], groupName, source };
      }
      const appKey = params.get('app');
      if (appKey?.trim()) {
        return { name: 'groupApp', from: parts[1], to: parts[2], groupName, source, appKey };
      }
    }
    return { name: 'notFound' };
  }
  if (parts[0] === 'week' && parts.length === 1) {
    return { name: 'thisWeek', ...view };
  }
  if (
    parts[0] === 'week' &&
    parts.length === 2 &&
    parts[1] !== undefined &&
    isValidDate(parts[1])
  ) {
    return { name: 'week', date: parts[1], ...view };
  }
  if (parts[0] !== 'day' || parts[1] === undefined || !isValidDate(parts[1])) {
    return { name: 'notFound' };
  }
  const date = parts[1];
  if (parts.length === 2) {
    return { name: 'day', date, ...view };
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
  trends: () => '/trends',
  settings: () => '/settings',
  group: (from: string, to: string, groupName: string, source: GroupSource) =>
    `/group/${from}/${to}?${new URLSearchParams({ name: groupName, source })}`,
  groupApp: (from: string, to: string, groupName: string, source: GroupSource, appKey: string) =>
    `/group/${from}/${to}/app?${new URLSearchParams({ name: groupName, source, app: appKey })}`,
  groupBack: (from: string, to: string, source: GroupSource) =>
    source === 'day'
      ? `/day/${from}?view=groups`
      : source === 'week'
        ? `/week/${from}?view=groups`
        : `/trends?view=groups&period=${Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86400000) + 1}`,
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

const getPathname = () => globalThis.location.pathname + globalThis.location.search;

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
