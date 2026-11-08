export function isValidNip(value: string | null | undefined): boolean {
  if (value == null || value === '') { return true; }
  if (!/^[0-9]{10}$/.test(value) || /^([0-9])\1{9}$/.test(value)) { return false; }
  const weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
  const sum = weights.reduce((total, weight, index) => total + Number(value[index]) * weight, 0);
  return sum % 11 === Number(value[9]);
}
