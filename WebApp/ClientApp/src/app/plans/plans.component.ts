import { Component, OnInit } from '@angular/core';
import { ApiService, Plan } from '../services/api.service';

@Component({
  selector: 'app-plans',
  templateUrl: './plans.component.html',
  styleUrls: ['./plans.component.css']
})
export class PlansComponent implements OnInit {
  plans: Plan[] = [];
  error = '';
  buying = false;

  constructor(private api: ApiService) {}

  get canReturnToDashboard(): boolean {
    const user = this.api.user();
    return !!user && (user.isAdmin || user.hasActivePlan);
  }

  ngOnInit() {
    this.api.plans().subscribe(x => {
      this.plans = x;
    });
  }

  buy(plan: Plan) {
    if (!plan.canPurchase || this.buying) { return; }
    this.buying = true;
    this.error = '';

    this.api.checkout(plan.code).subscribe(result => {
      if (result.url) { location.href = result.url; }
    }, err => {
      this.buying = false;
      this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie rozpoczac platnosci Stripe.';
    });
  }
}
