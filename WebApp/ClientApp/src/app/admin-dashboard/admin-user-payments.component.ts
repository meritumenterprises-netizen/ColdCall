import { Component, Input, OnChanges, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService, AdminUserPayment } from '../services/api.service';

@Component({
  selector: 'app-admin-user-payments',
  templateUrl: './admin-user-payments.component.html',
  styleUrls: ['./admin-user-payments.component.css']
})
export class AdminUserPaymentsComponent implements OnChanges, OnDestroy {
  @Input() userId: number;
  payments: AdminUserPayment[] = [];
  loading = false;
  error = '';
  sortBy = 'paidAtUtc';
  direction = 'desc';
  columns = [ { key: 'planCode', label: 'Plan' }, { key: 'paidAtUtc', label: 'Kupiono dnia' },
    { key: 'amountPaid', label: 'Zap\u0142acono' } ];
  private request: Subscription | null = null;

  constructor(private api: ApiService) {}
  ngOnChanges() { this.sortBy = 'paidAtUtc'; this.direction = 'desc'; this.load(); }
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.payments = []; this.error = ''; this.loading = true;
    this.request = this.api.adminUserPayments(this.userId).subscribe(rows => {
      this.payments = rows; this.loading = false;
    }, () => { this.error = 'Nie udalo sie pobrac platnosci.'; this.loading = false; });
  }
  sort(column: string) {
    if (this.loading || !this.columns.some(item => item.key === column)) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
  }
  get sortedPayments(): AdminUserPayment[] {
    return this.payments.map((payment, index) => ({ payment, index })).sort((first, second) => {
      const a = first.payment, b = second.payment;
      const comparison = this.sortBy === 'planCode' ? a.planCode.localeCompare(b.planCode, 'pl')
        : this.sortBy === 'amountPaid' ? a.amountPaid - b.amountPaid
        : new Date(a.paidAtUtc).getTime() - new Date(b.paidAtUtc).getTime();
      return comparison * (this.direction === 'asc' ? 1 : -1) || first.index - second.index;
    }).map(item => item.payment);
  }
}
