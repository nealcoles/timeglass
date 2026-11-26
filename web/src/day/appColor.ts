/** The colour of time not tied to a listed app: neutral, so it never looks like one of the apps. */
export const OTHER_COLOR = 'hsl(220 9% 60%)';

function appHue(appKey: string): number {
  let hash = 0x811c9dc5;
  for (let i = 0; i < appKey.length; i++) {
    hash ^= appKey.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0) % 360;
}

/** A deterministic colour for an app when there is no list to compare it with. */
export function appColor(appKey: string): string {
  return `hsl(${appHue(appKey)} 65% 50%)`;
}

/**
 * Assigns distinct colours to a set of apps or groups. Keys are sorted before assigning colours,
 * so a change in duration or display order does not change the result. Close hues are spread out
 * where space permits; after all 360 hues are used, lightness supplies further distinct colours.
 */
export function appColors(keys: readonly string[]): (key: string) => string {
  const sorted = [...new Set(keys)].sort();
  const colors = new Map<string, string>();
  const distances = Array<number>(360).fill(180);

  sorted.forEach((key, index) => {
    const preferred = appHue(key);
    if (index >= 360) {
      colors.set(key, `hsl(${preferred} 80% ${40 + ((index - 360) / sorted.length) * 20}%)`);
      return;
    }

    let hue = preferred;
    if ((distances[hue] ?? 0) < 24) {
      let greatest = -1;
      for (let candidate = 0; candidate < 360; candidate++) {
        const distance = distances[candidate] ?? 0;
        if (distance > greatest) {
          greatest = distance;
          hue = candidate;
        }
      }
    }
    colors.set(key, `hsl(${hue} 65% 50%)`);
    for (let candidate = 0; candidate < 360; candidate++) {
      const difference = Math.abs(candidate - hue);
      distances[candidate] = Math.min(distances[candidate] ?? 180, difference, 360 - difference);
    }
  });
  return (key) => {
    const color = colors.get(key);
    if (color === undefined) {
      throw new Error(`No colour assigned for ${key}.`);
    }
    return color;
  };
}
