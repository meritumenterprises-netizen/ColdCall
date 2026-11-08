import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, of, timer } from 'rxjs';
import { exhaustMap, switchMap, take, takeWhile, timeout } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-payment-success',
  template: `<main class="workspace"><h1>Płatność</h1>
    <p *ngIf="loading" role="status">Potwierdzanie płatności...</p>
    <p *ngIf="error" class="error" role="alert">{{ error }}</p>
    <button *ngIf="!loading" type="button" class="primary-button" (click)="confirm()">Sprawdź ponownie</button>
  </main>`
})
export class PaymentSuccessComponent implements OnInit, OnDestroy {
  loading = true;
  error = '';
  private request: Subscription | null = null;
  private paid = false;

  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router) {}
  ngOnInit() { this.confirm(); }

  confirm() {
    if (this.request) { this.request.unsubscribe(); }
    const id = this.route.snapshot.queryParams.session_id;
    if (!id) { this.loading = false; this.error = 'Brak identyfikatora płatności.'; return; }
    this.loading = true; this.error = ''; this.paid = false;
    this.request = timer(0, 2000).pipe(
      take(15),
      exhaustMap(() => this.api.confirmCheckout(id).pipe(timeout(120000))),
      takeWhile(result => !result.paid, true),
      switchMap(result => result.paid ? this.api.me(false).pipe(switchMap(() => of(true))) : of(false))
    ).subscribe(paid => {
      if (paid) {
        this.paid = true;
        this.api.rememberPurchasedPlan();
        this.router.navigateByUrl('/profile/plan', { replaceUrl: true });
      }
    }, err => {
      this.loading = false;
      this.error = err.error && err.error.message ? err.error.message : 'Nie udało się potwierdzić płatności. Spróbuj ponownie.';
    }, () => {
      this.loading = false;
      if (!this.paid) {
        this.error = 'Płatność nie została jeszcze potwierdzona. Sprawdź ponownie za chwilę.';
      }
    });
  }

  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
