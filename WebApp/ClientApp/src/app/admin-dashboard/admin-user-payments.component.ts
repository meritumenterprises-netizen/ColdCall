import { Component, Input, OnChanges, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
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
  columns = [ { key: 'paidAtUtc', label: 'Data' }, { key: 'planCode', label: 'Plan definition' },
    { key: 'amountPaid', label: 'Zapłacono' } ];
  private request: Subscription | null = null;
  private invoiceRequest: Subscription | null = null;
  downloadingInvoice: string | null = null;
  invoiceError = '';
  deletingPayment: string | null = null;
  deleteError = '';
  private deleteRequest: Subscription | null = null;

  get latestPayment(): AdminUserPayment | null {
    return this.payments.reduce((latest, payment) => !latest
      || Date.parse(payment.paidAtUtc) > Date.parse(latest.paidAtUtc) ? payment : latest, null);
  }

  deletePayment(payment: AdminUserPayment) {
    if (this.loading || this.deletingPayment !== null || payment === this.latestPayment) { return; }
    if (!window.confirm('Czy na pewno usun\u0105\u0107 zapis tej p\u0142atno\u015bci? Nie spowoduje to zwrotu pieni\u0119dzy ani usuni\u0119cia p\u0142atno\u015bci w Stripe.')) { return; }
    const userId = this.userId;
    this.deletingPayment = payment.stripeSessionId;
    this.deleteError = '';
    this.deleteRequest = this.api.deleteAdminUserPayment(userId, payment.stripeSessionId)
      .pipe(finalize(() => this.deletingPayment = null)).subscribe(() => {
        if (this.userId === userId) {
          this.payments = this.payments.filter(row => row.stripeSessionId !== payment.stripeSessionId);
        }
      }, err => {
        if (this.userId !== userId) { return; }
        this.deleteError = err.error && typeof err.error.message === 'string' ? err.error.message
          : err.status === 404 || err.status === 405 ? 'Nie znaleziono platnosci lub endpointu usuwania. Odswiez liste; jesli blad pozostaje, przebuduj i uruchom ponownie backend.'
          : err.status === 401 ? 'Sesja wygasla. Zaloguj sie ponownie.'
          : err.status === 403 ? 'Brak uprawnien administratora do usuniecia platnosci.'
          : 'Nie udalo sie usunac platnosci (HTTP ' + err.status + ').';
      });
  }

  downloadInvoice(payment: AdminUserPayment) {
    if (!payment.hasInvoice || this.downloadingInvoice !== null) { return; }
    this.downloadingInvoice = payment.stripeSessionId;
    this.invoiceError = '';
    this.invoiceRequest = this.api.adminUserInvoice(this.userId, payment.stripeSessionId)
      .pipe(finalize(() => this.downloadingInvoice = null)).subscribe(pdf => {
        const url = URL.createObjectURL(pdf);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'faktura-' + payment.planCode.replace(/[^A-Za-z0-9_-]/g, '') + '-' + payment.paidAtUtc.slice(0, 10) + '.pdf';
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      }, () => this.invoiceError = 'Nie udalo sie pobrac faktury.');
  }

  constructor(private api: ApiService) {}
  get totalAmountPaid(): number {
    return this.payments.reduce((total, payment) => total + Math.round(payment.amountPaid * 100), 0) / 100;
  }
  ngOnChanges() { this.sortBy = 'paidAtUtc'; this.direction = 'desc'; this.load(); }
  ngOnDestroy() {
    if (this.deleteRequest) { this.deleteRequest.unsubscribe(); }
    if (this.request) { this.request.unsubscribe(); }
    if (this.invoiceRequest) { this.invoiceRequest.unsubscribe(); }
  }
  load() {
    if (this.deleteRequest) { this.deleteRequest.unsubscribe(); }
    this.deleteError = '';
    if (this.invoiceRequest) { this.invoiceRequest.unsubscribe(); }
    this.invoiceError = '';
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
