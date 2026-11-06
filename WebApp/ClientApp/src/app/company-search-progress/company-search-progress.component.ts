import { Component, Input, OnChanges, OnDestroy } from '@angular/core';
import { Subscription, timer } from 'rxjs';

@Component({
  selector: 'app-company-search-progress',
  template: '<div *ngIf="active" class="search-progress" role="status">Wyszukiwanie {{ elapsedSeconds }} s</div>',
  styles: [`
    .search-progress {
      padding: 12px 14px;
      margin: 14px 0;
      border-radius: 6px;
      background: #dcfce7;
      color: #166534;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }
  `]
})
export class CompanySearchProgressComponent implements OnChanges, OnDestroy {
  @Input() active = false;
  @Input() startedAt = 0;
  elapsedSeconds = 0;
  private clock: Subscription | null = null;

  ngOnChanges() {
    this.stopClock();
    this.elapsedSeconds = 0;
    if (this.active) {
      this.clock = timer(0, 1000).subscribe(() => {
        this.elapsedSeconds = Math.max(0, Math.floor((Date.now() - this.startedAt) / 1000));
      });
    }
  }

  ngOnDestroy() { this.stopClock(); }

  private stopClock() {
    if (this.clock) { this.clock.unsubscribe(); this.clock = null; }
  }
}
