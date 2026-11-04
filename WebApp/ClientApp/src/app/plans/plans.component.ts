import { Component, OnInit } from '@angular/core';
import { of } from 'rxjs';
import { switchMap } from 'rxjs/operators';
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

  get planPurchaseDisabled(): boolean {
    const user = this.api.user();
    return !!user && !!user.disablePlanPurchase;
  }

  get hasHighestPlan(): boolean {
    const user = this.api.user();
    return !!user && !user.isAdmin && user.hasActivePlan && !!user.plan && user.plan.code.trim().split(/\s+to\s+/i).pop().toUpperCase() === 'PX100';
  }

  get canReturnToDashboard(): boolean {
    const user = this.api.user();
    return !!user && (user.isAdmin || user.hasActivePlan);
  }

  ngOnInit() {
    this.api.me(false).pipe(switchMap(() =>
      this.planPurchaseDisabled || this.hasHighestPlan ? of([]) : this.api.plans()
    )).subscribe(x => this.plans = x, () => this.error = 'Nie udalo sie pobrac planow.');
  }

  buy(plan: Plan) {
    if (this.planPurchaseDisabled || this.hasHighestPlan || !plan.canPurchase || this.buying) { return; }
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
