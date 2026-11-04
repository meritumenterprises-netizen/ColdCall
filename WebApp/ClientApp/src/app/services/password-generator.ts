export class PasswordGenerator {
  static generate(): string {
    const groups = ['ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz', '0123456789', '!@#$%^&*()-_=+?'];
    const alphabet = groups.join('');
    const limit = Math.floor(256 / alphabet.length) * alphabet.length;
    const bytes = new Uint8Array(32);
    let password: string;
    do {
      password = '';
      while (password.length < 12) {
        window.crypto.getRandomValues(bytes);
        for (let i = 0; i < bytes.length && password.length < 12; i++) {
          if (bytes[i] < limit) { password += alphabet[bytes[i] % alphabet.length]; }
        }
      }
    } while (!groups.every(group => password.split('').some(character => group.includes(character))));
    return password;
  }
}
