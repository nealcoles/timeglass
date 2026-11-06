import { useEffect } from 'react';
import { navigate } from '../router.ts';

/** True when a key press is meant for a text field, so ← and → move its cursor instead. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return (
    target.isContentEditable ||
    target.tagName === 'INPUT' ||
    target.tagName === 'TEXTAREA' ||
    target.tagName === 'SELECT'
  );
}

/**
 * Goes to `previous` on ← and to `next` on →, anywhere on the page. A key press that something
 * else already handled (the timeline moves between its blocks with the arrow keys), one with a
 * modifier, or one in a text field is left alone. `null` turns that direction off.
 */
export function useArrowKeys(previous: string | null, next: string | null) {
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (
        event.defaultPrevented ||
        event.altKey ||
        event.ctrlKey ||
        event.metaKey ||
        event.shiftKey ||
        isTyping(event.target)
      ) {
        return;
      }
      const path = event.key === 'ArrowLeft' ? previous : event.key === 'ArrowRight' ? next : null;
      if (path === null) {
        return;
      }
      event.preventDefault();
      navigate(path);
    }
    globalThis.addEventListener('keydown', onKeyDown);
    return () => globalThis.removeEventListener('keydown', onKeyDown);
  }, [previous, next]);
}
