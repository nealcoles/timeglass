import { ApiError, errorMessage, isUnauthorized, unwrap } from './errors.ts';
import { shouldRetry } from './queryClient.ts';

const result = (status: number, body?: unknown) => ({
  ...(status < 400 ? { data: body } : { error: body }),
  response: new Response(null, { status }),
});

describe('unwrap', () => {
  it('returns the data of a success', async () => {
    await expect(unwrap(Promise.resolve(result(200, { ok: 1 })))).resolves.toEqual({ ok: 1 });
  });

  it('throws an ApiError with the problem details otherwise', async () => {
    const error = await unwrap(Promise.resolve(result(400, { title: 'Bad', detail: 'No.' }))).catch(
      (e: unknown) => e,
    );
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 400, message: 'No.', problem: { title: 'Bad' } });
  });

  it('ignores a body that is not a problem', async () => {
    const error = await unwrap(Promise.resolve(result(502, 'Bad gateway'))).catch(
      (e: unknown) => e,
    );
    expect(error).toMatchObject({ status: 502, problem: null, message: 'The API answered 502.' });
  });
});

describe('errorMessage', () => {
  it.each([
    [new ApiError(401, null), 'Open the dashboard from the Timeglass tray icon to sign in.'],
    [
      new ApiError(500, { detail: 'Stack trace' }),
      'Timeglass ran into a problem. Try again in a moment.',
    ],
    [new ApiError(400, { title: 'Use yyyy-MM-dd.' }), 'Use yyyy-MM-dd.'],
    [new TypeError('Failed to fetch'), 'Timeglass is not running, or could not be reached.'],
  ])('describes %s', (error, message) => {
    expect(errorMessage(error)).toBe(message);
  });
});

describe('isUnauthorized', () => {
  it('is true only for a 401 ApiError', () => {
    expect(isUnauthorized(new ApiError(401, null))).toBe(true);
    expect(isUnauthorized(new ApiError(403, null))).toBe(false);
    expect(isUnauthorized(new Error('401'))).toBe(false);
  });
});

describe('shouldRetry', () => {
  it('does not retry client errors', () => {
    expect(shouldRetry(0, new ApiError(400, null))).toBe(false);
    expect(shouldRetry(0, new ApiError(401, null))).toBe(false);
  });

  it('retries server and network errors twice', () => {
    expect(shouldRetry(0, new ApiError(503, null))).toBe(true);
    expect(shouldRetry(1, new TypeError('Failed to fetch'))).toBe(true);
    expect(shouldRetry(2, new TypeError('Failed to fetch'))).toBe(false);
  });
});
