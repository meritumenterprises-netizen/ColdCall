import { Component, Input, OnChanges, OnDestroy } from '@angular/core';
import { Subscription, timer } from 'rxjs';

@Component({
  selector: 'app-company-search-progress',
  template: `
    <div *ngIf="active || finishedAt > 0" class="search-progress" [class.completed]="!active" role="status">
      {{ active ? 'Wyszukiwanie' : 'Załadowano w' }} <span aria-hidden="true">{{ active ? elapsedTime : (elapsedMilliseconds / 1000).toFixed(3) }}</span><ng-container *ngIf="!active"> sekundy, znaleziono {{ formattedRecordCount }} rekordów</ng-container>
    </div>
  `,
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
    .search-progress.completed {
      background: #f1f3f5;
      color: #495057;
    }
  `]
})
export class CompanySearchProgressComponent implements OnChanges, OnDestroy {
  @Input() active = false;
  @Input() startedAt = 0;
  @Input() finishedAt = 0;
  @Input() recordCount = 0;
  elapsedMilliseconds = 0;
  private clock: Subscription | null = null;

  ngOnChanges() {
    this.stopClock();
    this.elapsedMilliseconds = this.finishedAt > 0 ? Math.max(0, this.finishedAt - this.startedAt) : 0;
    if (this.active) {
      this.clock = timer(0, 1000).subscribe(() => {
        this.elapsedMilliseconds = Math.max(0, Date.now() - this.startedAt);
      });
    }
  }

  ngOnDestroy() { this.stopClock(); }

  get formattedRecordCount() {
    return this.recordCount.toLocaleString('en-US').replace(/,/g, ' ');
  }

  get elapsedTime() {
    const seconds = Math.floor(this.elapsedMilliseconds / 1000);
    const milliseconds = this.active ? '000' : String(this.elapsedMilliseconds % 1000).padStart(3, '0');
    return seconds + ':' + milliseconds;
  }

  private stopClock() {
    if (this.clock) { this.clock.unsubscribe(); this.clock = null; }
  }
}
