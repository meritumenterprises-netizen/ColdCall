import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { AdminPlanDefinition, ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-plans', templateUrl: './admin-plans.component.html', styleUrls: ['./admin-plans.component.css']
})
export class AdminPlansComponent implements OnInit, OnDestroy {
  plans: AdminPlanDefinition[] = [];
  draft: AdminPlanDefinition | null = null;
  upgrade = false;
  loading = false;
  saving = false;
  error = '';
  saved = false;
  descriptionTab: 'preview' | 'html' = 'preview';
  private loadRequest: Subscription;
  private saveRequest: Subscription;
  private original: AdminPlanDefinition | null = null;

  constructor(private api: ApiService) {}
  ngOnInit() { this.loadPlans(); }
  get dirty() { return !!this.draft && JSON.stringify(this.draft) !== JSON.stringify(this.original); }
  loadPlans() {
    if (this.saving) { return; }
    if (this.loadRequest) { this.loadRequest.unsubscribe(); }
    this.loading = true; this.error = ''; this.saved = false; this.draft = null; this.original = null;
    this.loadRequest = this.api.adminPlans(this.upgrade).pipe(finalize(() => this.loading = false)).subscribe(plans => {
      this.plans = plans;
      if (plans.length) { this.selectPlan(plans[0]); }
    }, () => { this.plans = []; this.error = 'Nie udalo sie pobrac planow.'; });
  }
  selectPlan(plan: AdminPlanDefinition) {
    if (this.saving) { return; }
    this.original = { ...plan }; this.draft = { ...plan }; this.error = ''; this.saved = false;
    this.descriptionTab = 'preview';
  }
  cancel() {
    if (!this.saving && this.original) { this.draft = { ...this.original }; this.error = ''; this.saved = false; }
  }
  save() {
    if (!this.draft || !this.dirty || this.saving || !this.draft.code.trim()) { return; }
    const plan = { ...this.draft, code: this.draft.code.trim() };
    this.saving = true; this.error = ''; this.saved = false;
    this.saveRequest = this.api.saveAdminPlan(plan).pipe(finalize(() => this.saving = false)).subscribe(() => {
      this.plans = this.plans.map(p => p.id === plan.id ? plan : p);
      this.original = { ...plan }; this.draft = { ...plan }; this.saved = true;
    }, err => this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie zapisac planu.');
  }
  ngOnDestroy() {
    if (this.loadRequest) { this.loadRequest.unsubscribe(); }
    if (this.saveRequest) { this.saveRequest.unsubscribe(); }
  }
}
