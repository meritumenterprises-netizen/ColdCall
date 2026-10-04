import { Component, OnInit } from '@angular/core';
import { ApiService, Plan } from '../services/api.service';

@Component({
  selector: 'app-plans',
  templateUrl: './plans.component.html'
})
export class PlansComponent implements OnInit {
  plans: Plan[] = [];
  hasActivePlan = false;
  error = '';

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.api.plans().subscribe(x => {
      this.plans = x;
      this.hasActivePlan = x.some(plan => !!plan.activeUntil);
    });
  }

  buy(plan: Plan) {
    if (this.hasActivePlan) { return; }

    this.api.checkout(plan.code).subscribe(result => {
      if (result.url) { location.href = result.url; }
    }, err => this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie rozpoczac platnosci Stripe.');
  }
}
