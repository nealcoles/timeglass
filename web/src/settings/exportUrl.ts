export type ExportFormat = 'csv' | 'json';
export type ExportGranularity = 'intervals' | 'daily-apps';

/** The API address that answers with the export file for these choices. */
export function exportUrl(
  from: string,
  to: string,
  format: ExportFormat,
  granularity: ExportGranularity,
): string {
  return `/api/export?${new URLSearchParams({ from, to, format, granularity }).toString()}`;
}
