/** The fields of an RFC 9457 problem response that the dashboard uses. */
export interface ProblemDetails {
  title?: string | null;
  detail?: string | null;
  status?: number | null;
  errors?: Record<string, string[]>;
}

/** A response from the API that wasn't a success. */
export class ApiError extends Error {
  override readonly name = 'ApiError';

  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | null,
  ) {
    super(problem?.detail ?? problem?.title ?? `The API answered ${status}.`);
  }

  /** True when the browser isn't signed in: the cookie is missing or from an earlier run. */
  get isUnauthorized(): boolean {
    return this.status === 401;
  }
}

export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.isUnauthorized;
}

/** What openapi-fetch resolves to. */
interface FetchResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

/**
 * Returns the data of a successful response and throws {@link ApiError} otherwise, so TanStack
 * Query sees every failure the same way. Network errors are thrown as they are.
 */
export async function unwrap<T>(request: Promise<FetchResult<T>>): Promise<T> {
  const { data, error, response } = await request;
  if (!response.ok) {
    throw new ApiError(response.status, toProblem(error));
  }
  return data as T;
}

function toProblem(body: unknown): ProblemDetails | null {
  return typeof body === 'object' && body !== null ? (body as ProblemDetails) : null;
}

/** A sentence to show the user for any error a query can fail with. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isUnauthorized) {
      return 'Open the dashboard from the Timeglass tray icon to sign in.';
    }
    if (error.status >= 500) {
      return 'Timeglass ran into a problem. Try again in a moment.';
    }
    return error.message;
  }
  return 'Timeglass is not running, or could not be reached.';
}
