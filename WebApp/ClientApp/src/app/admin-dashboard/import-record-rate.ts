import { AdminImport } from '../services/api.service';

export function importRecordsPerSecond(row: AdminImport): number | null {
  if (row.importTargetRecords == null || !Number.isFinite(row.importTargetRecords)
      || row.importTargetRecords < 0 || !row.duration) { return null; }
  const seconds = importDurationSeconds(row.duration);
  return seconds !== null && seconds > 0 ? row.importTargetRecords / seconds : null;
}

export function importDurationSeconds(value: string): number | null {
  if (!value) { return null; }
  const duration = /^(?:(\d+)\.)?(\d+):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?$/.exec(value.trim());
  if (!duration || (duration[1] != null && Number(duration[2]) > 23)) { return null; }
  const seconds = Number(duration[1] || 0) * 86400 + Number(duration[2]) * 3600
    + Number(duration[3]) * 60 + Number(duration[4]) + Number('0.' + (duration[5] || '0'));
  return Number.isFinite(seconds) ? seconds : null;
}
