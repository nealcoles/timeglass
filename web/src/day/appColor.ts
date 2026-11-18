/** The colour of time not tied to a listed app: neutral, so it never looks like one of the apps. */
export const OTHER_COLOR = 'hsl(220 9% 60%)';

/**
 * A colour for an app that stays the same across days and views: the hue comes from an FNV-1a
 * hash of the app key, so the same app is always the same colour and no palette has to be stored.
 */
export function appColor(appKey: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < appKey.length; i++) {
    hash ^= appKey.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  const hue = (hash >>> 0) % 360;
  return `hsl(${hue} 65% 50%)`;
}
