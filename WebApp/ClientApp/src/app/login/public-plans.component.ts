import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize, timeout } from 'rxjs/operators';
import { ApiService, PublicPlan } from '../services/api.service';

@Component({
  selector: 'app-public-plans',
  templateUrl: './public-plans.component.html',
  styleUrls: ['../plans/plans.component.css'],
  styles: [`.public-plans { width: 100%; max-width: 1600px; margin: 0 auto; padding: 32px 24px; box-sizing: border-box; }
    .public-plans .plan-grid { grid-template-columns: repeat(5, minmax(0, 1fr)); }
    .public-plans .plan-card { min-width: 0; overflow-wrap: anywhere; }
    @media (max-width: 1100px) { .public-plans .plan-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 600px) { .public-plans .plan-grid { grid-template-columns: 1fr; } }`]
})
export class PublicPlansComponent implements OnInit, OnDestroy {
  plans: PublicPlan[] = [];
  loading = false;
  error = '';
  private request: Subscription | null = null;
  constructor(private api: ApiService) {}
  ngOnInit() { this.load(); }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.request = this.api.publicPlans().pipe(timeout(15000), finalize(() => this.loading = false))
      .subscribe(plans => this.plans = plans, () => this.error = 'Nie udalo sie pobrac planow. Sprobuj ponownie.');
  }
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
