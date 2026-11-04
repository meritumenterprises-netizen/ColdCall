import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { AdminPlanDefinition, ApiService } from '../services/api.service';

type PlanSortKey = 'code' | 'maxNumberOfSearches' | 'maxNumberOfCSVExportsPerSearch' | 'maxNumberOfExportedRecords' | 'expiresAfterDays' | 'price';

@Component({
  selector: 'app-admin-plans', templateUrl: './admin-plans.component.html', styleUrls: ['./admin-plans.component.css']
})
export class AdminPlansComponent implements OnInit, OnDestroy {
  plans: AdminPlanDefinition[] = [];
  sortBy: PlanSortKey = 'code';
  sortDirection: 'asc' | 'desc' = 'asc';
  sortableColumns: { key: PlanSortKey; label: string }[] = [
    { key: 'code', label: 'Nazwa planu' },
    { key: 'maxNumberOfSearches', label: 'Wyszukiwania' },
    { key: 'maxNumberOfCSVExportsPerSearch', label: 'Eksporty CSV / wyszukiwanie' },
    { key: 'maxNumberOfExportedRecords', label: 'Rekordy' },
    { key: 'expiresAfterDays', label: 'Dni' },
    { key: 'price', label: 'Cena' }
  ];

  sortPlans(key: PlanSortKey) {
    if (this.loading || this.saving) { return; }
    this.sortDirection = this.sortBy === key && this.sortDirection === 'asc' ? 'desc' : 'asc';
    this.sortBy = key;
  }

  get sortedPlans(): AdminPlanDefinition[] {
    return this.plans.slice().sort((a, b) => {
      const first = a[this.sortBy], second = b[this.sortBy];
      if (first == null || second == null) {
        return first == null ? (second == null ? a.id - b.id : 1) : -1;
      }
      const comparison = this.sortBy === 'code'
        ? String(first).localeCompare(String(second), 'pl', { numeric: true, sensitivity: 'base' })
        : Number(first) - Number(second);
      return (this.sortDirection === 'asc' ? comparison : -comparison) || a.id - b.id;
    });
  }

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
      if (plans.length) { this.selectPlan(this.sortedPlans[0]); }
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
  updateOffer(plan: AdminPlanDefinition, event: Event) {
    const checkbox = event.target as HTMLInputElement;
    if (this.saving || this.loading) { checkbox.checked = plan.showInOffer; return; }
    const showInOffer = checkbox.checked;
    this.saving = true; this.error = ''; this.saved = false;
    this.saveRequest = this.api.updateAdminPlanOffer(plan.id, showInOffer)
      .pipe(finalize(() => this.saving = false)).subscribe(() => {
        plan.showInOffer = showInOffer;
        if (this.original && this.original.id === plan.id) { this.original.showInOffer = showInOffer; }
        if (this.draft && this.draft.id === plan.id) { this.draft.showInOffer = showInOffer; }
        this.saved = true;
      }, err => {
        checkbox.checked = plan.showInOffer;
        this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie zmienic widocznosci planu w ofercie.';
      });
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
