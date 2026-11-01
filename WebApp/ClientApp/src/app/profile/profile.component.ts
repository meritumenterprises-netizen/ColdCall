import { ChangeDetectorRef, Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, CurrentPlan, StripePurchase, UserProfile, ProfileDetails } from '../services/api.service';

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
  details: ProfileDetails = { firstName: '', lastName: '', companyName: '', nip: '', postalCode: '', city: '', street: '', streetNumber: '' };
  saving = false;
  saveError = '';
  saveMessage = '';
  private saveRequest: Subscription | null = null;
  detailFields = [
    { key: 'firstName', label: 'Imi\u0119', max: 100, autocomplete: 'given-name' },
    { key: 'lastName', label: 'Nazwisko', max: 100, autocomplete: 'family-name' },
    { key: 'companyName', label: 'Nazwa firmy', max: 200, autocomplete: 'organization' },
    { key: 'nip', label: 'NIP', max: 10, autocomplete: 'off' },
    { key: 'postalCode', label: 'Kod pocztowy', max: 6, autocomplete: 'postal-code' },
    { key: 'city', label: 'Miasto', max: 100, autocomplete: 'address-level2' },
    { key: 'street', label: 'Ulica', max: 200, autocomplete: 'address-line1' },
    { key: 'streetNumber', label: 'Nr ulicy/lokalu', max: 50, autocomplete: 'address-line2' }
  ];

  saveDetails() {
    if (this.saving) { return; }
    this.saveError = ''; this.saveMessage = '';
    if (this.detailFields.some(field => field.key !== 'companyName' && field.key !== 'nip' && !this.details[field.key].trim())
      || (!!this.details.nip && !/^[0-9]{10}$/.test(this.details.nip))
      || !/^[0-9]{2}-[0-9]{3}$/.test(this.details.postalCode)) {
      this.saveError = 'Uzupelnij wymagane pola, kod pocztowy (00-000) oraz NIP (10 cyfr), jesli podano.'; return;
    }
    const details = { ...this.details };
    this.saving = true;
    this.saveRequest = this.api.updateProfile(details).pipe(finalize(() => this.saving = false)).subscribe(() => {
      if (this.profile) { Object.assign(this.profile, details, { displayName: details.firstName.trim() + ' ' + details.lastName.trim() }); }
      this.saveMessage = 'Dane zostaly zapisane.';
    }, () => this.saveError = 'Nie udalo sie zapisac danych. Sprobuj ponownie.');
  }
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
      this.detailFields.forEach(field => this.details[field.key] = user[field.key] || '');
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
    if (this.saveRequest) { this.saveRequest.unsubscribe(); }
    if (this.request) { this.request.unsubscribe(); }
    if (this.purchasesRequest) { this.purchasesRequest.unsubscribe(); }
  }
}
