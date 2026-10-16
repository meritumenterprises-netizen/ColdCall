import { ChangeDetectorRef, Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, CurrentPlan, StripePurchase, UserProfile } from '../services/api.service';

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.css']
})
export class ProfileComponent implements OnInit, OnDestroy {
  @ViewChild('purchaseDialog', { static: true }) purchaseDialog: ElementRef;
  purchasedPlan: CurrentPlan | null = null;
  plan: CurrentPlan | null = null;
  profile: UserProfile | null = null;
  loading = true;
  error = '';
  private request: Subscription | null = null;
  purchases: StripePurchase[] = [];
  purchasesLoading = false;
  purchasesError = '';
  purchasesSortBy = 'paidAtUtc';
  purchasesDirection = 'desc';
  purchaseColumns = [
    { key: 'planCode', label: 'Plan' },
    { key: 'maxNumberOfSearches', label: 'Max. liczba wyszukań' },
    { key: 'maxNumberOfCSVExportsPerSearch', label: 'Max. liczba eksportów CSV/wyszukanie' },
    { key: 'maxNumberOfExportedRecords', label: 'Max. liczba rekordów do wyszukania' },
    { key: 'amountPaid', label: 'Kwota' },
    { key: 'paidAtUtc', label: 'Data zakupu' }
  ];
  private purchasesRequest: Subscription | null = null;

  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router,
    private changeDetector: ChangeDetectorRef) {}

  acknowledgePurchase() {
    this.purchaseDialog.nativeElement.close();
    this.router.navigateByUrl('/dashboard');
  }

  get isDataPanel() { return this.route.snapshot.data.section === 'data'; }
  get isPurchasesPanel() { return this.route.snapshot.data.section === 'purchases'; }
  get isPlanPanel() { return !this.isDataPanel && !this.isPurchasesPanel; }

  formatAmount(amount: number) { return amount.toLocaleString('pl-PL', { style: 'currency', currency: 'PLN' }); }
  formatLimit(value: number | null | undefined) { return value == null ? '-' : value.toLocaleString('pl-PL'); }

  sortPurchases(key: string) {
    this.purchasesDirection = this.purchasesSortBy === key && this.purchasesDirection === 'asc' ? 'desc' : 'asc';
    this.purchasesSortBy = key;
    this.loadPurchases();
  }

  loadPurchases() {
    if (!this.profile || this.profile.isAdmin) { return; }
    if (this.purchasesRequest) { this.purchasesRequest.unsubscribe(); }
    this.purchasesLoading = true;
    this.purchasesError = '';
    this.purchasesRequest = this.api.purchases(this.purchasesSortBy, this.purchasesDirection)
      .pipe(finalize(() => this.purchasesLoading = false)).subscribe(rows => this.purchases = rows,
        () => this.purchasesError = 'Nie udało się pobrać zakupów.');
  }

  get canSearch(): boolean {
    const user = this.api.user();
    return !!this.profile && !this.profile.isAdmin && !!user && user.hasActivePlan;
  }

  ngOnInit() { this.loadPlan(); }

  loadPlan() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.request = this.api.profile().pipe(finalize(() => this.loading = false)).subscribe(user => {
      this.profile = user;
      this.plan = user.plan || null;
      if (this.isPurchasesPanel) {
        if (user.isAdmin) { this.router.navigateByUrl('/profile/data', { replaceUrl: true }); return; }
        this.loadPurchases();
      }
      if (this.isPlanPanel && !user.isAdmin) {
        const purchase = this.api.takePurchasedPlan();
        this.purchasedPlan = purchase && this.plan && purchase.code === this.plan.code ? this.plan : purchase;
        if (this.purchasedPlan) {
          this.changeDetector.detectChanges();
          this.purchaseDialog.nativeElement.showModal();
        }
      }
    }, () => this.error = 'Nie udało się pobrać danych profilu.');
  }

  ngOnDestroy() {
    if (this.request) { this.request.unsubscribe(); }
    if (this.purchasesRequest) { this.purchasesRequest.unsubscribe(); }
  }
}
