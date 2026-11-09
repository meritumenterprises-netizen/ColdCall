import { Component } from '@angular/core';
import { RateLimitToastService } from './services/rate-limit-toast.service';

@Component({
  selector: 'app-rate-limit-toast',
  template: '<div *ngIf="toast.message | async as message" class="rate-limit-toast" role="alert"><span>{{ message }}</span><button type="button" aria-label="Zamknij komunikat" title="Zamknij" (click)="toast.dismiss()">&#215;</button></div>',
  styles: ['.rate-limit-toast { position: fixed; right: 24px; bottom: 24px; z-index: 2000; display: flex; align-items: center; gap: 16px; max-width: calc(100vw - 48px); box-sizing: border-box; padding: 12px 16px; border-radius: 6px; background: #991b1b; color: #fff; box-shadow: 0 4px 16px #0003; } .rate-limit-toast span { min-width: 0; overflow-wrap: anywhere; } button { flex-shrink: 0; width: 24px; height: 24px; border: 0; background: transparent; color: inherit; font-size: 22px; cursor: pointer; }']
})
export class RateLimitToastComponent {
  constructor(public toast: RateLimitToastService) {}
}
