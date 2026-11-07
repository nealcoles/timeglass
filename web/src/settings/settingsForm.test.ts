import { settings } from '../test/fixtures.ts';
import { addKey, toForm, toSettings, validate } from './settingsForm.ts';

describe('settings form', () => {
  it('round-trips the settings', () => {
    const dto = settings({
      idleThresholdSeconds: 90,
      idleExemptApps: ['vlc'],
      excludedApps: ['keepass'],
      recordWindowTitles: false,
      appsWithoutTitles: ['edge'],
      appAliases: [{ key: 'game', name: 'Game', exes: ['a.exe', 'b.exe'], titlePattern: null }],
      startWithWindows: true,
    });

    const form = toForm(dto);

    expect(form.idleMinutes).toBe('1.5');
    expect(form.aliases[0]?.exes).toBe('a.exe, b.exe');
    expect(toSettings(form)).toEqual(dto);
  });

  it('turns idle detection off and back on with five minutes', () => {
    const form = toForm(settings({ idleThresholdSeconds: null }));

    expect(form.idleEnabled).toBe(false);
    expect(form.idleMinutes).toBe('5');
    expect(toSettings(form).idleThresholdSeconds).toBeNull();
    expect(toSettings({ ...form, idleEnabled: true }).idleThresholdSeconds).toBe(300);
  });

  it('reads minutes with a decimal comma', () => {
    const form = { ...toForm(settings()), idleMinutes: '2,5' };

    expect(toSettings(form).idleThresholdSeconds).toBe(150);
    expect(validate(form)).toEqual({});
  });

  it.each(['', 'abc', '0.25', '241'])('rejects %j minutes', (minutes) => {
    const form = { ...toForm(settings()), idleMinutes: minutes };

    expect(validate(form).idleThresholdSeconds).toEqual([
      'Enter a number of minutes from 0.5 to 240.',
    ]);
  });

  it('ignores the minutes while idle detection is off', () => {
    const form = { ...toForm(settings()), idleEnabled: false, idleMinutes: 'abc' };

    expect(validate(form)).toEqual({});
  });

  it('rejects an app both excluded and without titles', () => {
    const form = {
      ...toForm(settings()),
      excludedApps: ['Edge', 'keepass'],
      appsWithoutTitles: ['edge'],
    };

    expect(validate(form).appsWithoutTitles?.[0]).toMatch(/^Edge is also excluded/);
  });

  it('needs a key for every alias and drops empty executables', () => {
    const form = {
      ...toForm(settings()),
      aliases: [{ key: ' ', name: 'Game', exes: 'a.exe, , b.exe,', titlePattern: ' ' }],
    };

    expect(validate(form).appAliases).toEqual(['Row 1: give the app a key, such as "game".']);
    expect(toSettings(form).appAliases).toEqual([
      { key: '', name: 'Game', exes: ['a.exe', 'b.exe'], titlePattern: null },
    ]);
  });

  it('adds a key once, trimmed', () => {
    expect(addKey(['vlc'], ' zoom ')).toEqual(['vlc', 'zoom']);
    expect(addKey(['vlc'], 'VLC')).toEqual(['vlc']);
    expect(addKey(['vlc'], '  ')).toEqual(['vlc']);
  });
});
