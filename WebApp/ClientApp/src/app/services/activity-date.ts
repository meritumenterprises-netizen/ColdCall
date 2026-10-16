export function isFutureActivityDate(value: string | null | undefined, today: Date = new Date()): boolean {
  if (!value) { return false; }
  const date = new Date(value);
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  return !isNaN(date.getTime()) && date >= tomorrow;
}
