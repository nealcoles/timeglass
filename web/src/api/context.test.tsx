import { renderHook } from '@testing-library/react';
import { useApiClient } from './context.ts';

describe('useApiClient', () => {
  it('needs an ApiProvider', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => renderHook(() => useApiClient())).toThrow(/inside <ApiProvider>/);
  });
});
