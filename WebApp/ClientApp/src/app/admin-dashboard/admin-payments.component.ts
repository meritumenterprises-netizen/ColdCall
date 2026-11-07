import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService, AppUser } from '../services/api.service';

interface PaymentUserRow {
  id: number;
  displayName: string;
  googleEmail: string;
  planCode: string;
  expiresAtUtc: string;
  paidAtUtc: string;
  amountPaid: number | null;
  expired: boolean;
}

@Component({
  selector: 'app-admin-payments',
  templateUrl: './admin-payments.component.html',
  styleUrls: ['./admin-payments.component.css']
})
export class AdminPaymentsComponent implements OnInit, OnDestroy {
  users: AppUser[] = [];
  loading = false;
  error = '';
  page = 1;
  readonly pageSize = 10;
  sortBy = 'displayName';
  direction = 'asc';
  selectedUser: PaymentUserRow | null = null;
  readonly columns = [
    { key: 'displayName', label: 'Imię i nazwisko' },
    { key: 'googleEmail', label: 'Email' },
    { key: 'planCode', label: 'Aktualny Plan' },
    { key: 'paidAtUtc', label: 'Zakupiono' },
    { key: 'amountPaid', label: 'Zapłacono' },
    { key: 'expiresAtUtc', label: 'Data wygaśnięcia' }
  ];
  private request: Subscription | null = null;
  constructor(private api: ApiService) {}
  ngOnInit() { this.load(); }
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }

  private dateUtc(value: string) { return /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z'; }
  get rows(): PaymentUserRow[] {
    const now = Date.now();
    return this.users.map(user => {
      const plans = (user.plans || []).slice().sort((a, b) =>
        new Date(this.dateUtc(b.expiresAtUtc)).getTime() - new Date(this.dateUtc(a.expiresAtUtc)).getTime());
      const plan = plans[0];
      const expiresAtUtc = plan ? this.dateUtc(plan.expiresAtUtc) : '';
      const paidAtUtc = plan && plan.paidAtUtc ? this.dateUtc(plan.paidAtUtc) : '';
      return { id: user.id,
        displayName: [user.firstName, user.lastName].filter(value => value && value.trim()).join(' ').trim() || user.displayName || '',
        googleEmail: user.googleEmail, planCode: plan ? plan.planCode : '', expiresAtUtc, paidAtUtc,
        amountPaid: plan && plan.amountPaid != null ? plan.amountPaid : null,
        expired: !!expiresAtUtc && new Date(expiresAtUtc).getTime() < now };
    }).sort((a, b) => {
      let comparison: number;
      if (this.sortBy === 'expiresAtUtc' || this.sortBy === 'paidAtUtc') {
        const first = a[this.sortBy], second = b[this.sortBy];
        if (!first || !second) { return first ? -1 : second ? 1 : a.id - b.id; }
        comparison = new Date(first).getTime() - new Date(second).getTime();
      } else if (this.sortBy === 'amountPaid') {
        if (a.amountPaid == null || b.amountPaid == null) { return a.amountPaid == null ? (b.amountPaid == null ? a.id - b.id : 1) : -1; }
        comparison = a.amountPaid - b.amountPaid;
      } else {
        comparison = a[this.sortBy].localeCompare(b[this.sortBy], 'pl');
      }
      return comparison * (this.direction === 'asc' ? 1 : -1) || a.id - b.id;
    });
  }
  get totalPages() { return Math.max(1, Math.ceil(this.users.length / this.pageSize)); }
  get pagedRows() { return this.rows.slice((this.page - 1) * this.pageSize, this.page * this.pageSize); }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true; this.error = ''; this.selectedUser = null;
    this.request = this.api.users().subscribe(users => {
      this.users = users.filter(user => {
        if (user.isBetaUser) { return false; }
        const plan = (user.plans || []).slice().sort((a, b) =>
          new Date(this.dateUtc(b.expiresAtUtc)).getTime() - new Date(this.dateUtc(a.expiresAtUtc)).getTime())[0];
        const code = plan && (plan.planCode || '').trim().toUpperCase();
        return !!code && code !== 'PX0';
      });
      this.page = Math.min(this.page, this.totalPages); this.loading = false;
    }, () => {
      this.users = []; this.loading = false;
      this.error = 'Nie udało się pobrać użytkowników.';
    });
  }
  sort(column: string) {
    if (this.loading || !this.columns.some(item => item.key === column)) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column; this.page = 1; this.selectedUser = null;
  }
  changePage(page: number) {
    if (this.loading) { return; }
    this.page = Math.max(1, Math.min(page, this.totalPages));
    this.selectedUser = null;
  }
  selectUser(row: PaymentUserRow, event?: KeyboardEvent) {
    if (event) { event.preventDefault(); }
    this.selectedUser = row;
  }
  formatExpiry(value: string): string {
    if (!value) { return '-'; }
    const expiration = new Date(value);
    if (!Number.isFinite(expiration.getTime())) { return '-'; }
    const now = new Date();
    const days = Math.round((Date.UTC(expiration.getFullYear(), expiration.getMonth(), expiration.getDate())
      - Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())) / 86400000);
    if (days === 0) { return 'dzisiaj'; }
    if (days > 0) { return days === 1 ? 'za 1 dzień' : 'za ' + days + ' dni'; }
    return days === -1 ? '1 dzień temu' : Math.abs(days) + ' dni temu';
  }
  trackUser(index: number, row: PaymentUserRow) { return row.id; }
}
