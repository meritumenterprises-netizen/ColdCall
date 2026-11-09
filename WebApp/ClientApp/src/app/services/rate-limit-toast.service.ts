import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class RateLimitToastService {
  readonly message = new BehaviorSubject<string>('');
  private timer: number | null = null;

  show() {
    if (this.timer !== null) { window.clearTimeout(this.timer); }
    this.message.next('Zbyt wiele \u017c\u0105da\u0144. Spr\u00f3buj ponownie za chwil\u0119.');
    this.timer = window.setTimeout(() => this.dismiss(), 6000);
  }

  dismiss() {
    if (this.timer !== null) { window.clearTimeout(this.timer); }
    this.timer = null;
    this.message.next('');
  }
}
