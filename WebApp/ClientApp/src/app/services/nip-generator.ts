import { isValidNip } from './nip-validation';

export class NipGenerator {
  static generate(): string {
    const weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
    const bytes = new Uint8Array(16);
    while (true) {
      let digits = '';
      while (digits.length < 9) {
        window.crypto.getRandomValues(bytes);
        for (let index = 0; index < bytes.length && digits.length < 9; index++) {
          if (bytes[index] >= 250) { continue; }
          const digit = bytes[index] % 10;
          if (!digits.length && digit === 0) { continue; }
          digits += digit;
        }
      }
      const checksum = weights.reduce((sum, weight, index) => sum + Number(digits[index]) * weight, 0) % 11;
      if (checksum === 10) { continue; }
      const nip = digits + checksum;
      if (isValidNip(nip)) { return nip; }
    }
  }
}
