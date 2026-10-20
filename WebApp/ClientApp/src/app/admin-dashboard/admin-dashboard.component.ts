import { Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { isFutureActivityDate } from '../services/activity-date';
import { Subscription, timer } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { AddressLevel, AddressOption, ApiService, AppUser, BusinessType, Company, CompanyFilters, Pkd, SortableColumn } from '../services/api.service';

@Component({
  selector: 'app-admin-dashboard',
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.css']
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  isFutureActivityDate = isFutureActivityDate;
  updateCompanyNotes(event: { companyId: number; notes: string }) {
    const company = this.companies.find(row => row.id === event.companyId);
    if (company) { company.userNotes = event.notes; }
  }
  @ViewChild('deleteCsvDialog', { static: true }) deleteCsvDialog: ElementRef;
  busyUserIds = new Set<number>();
  userToDelete: { id: number; googleEmail: string } | null = null;
  private userActionRequests = new Set<Subscription>();
  extendPlan(id: number) {
    if (this.busyUserIds.has(id)) { return; }
    this.busyUserIds.add(id);
    const request = this.api.extendUserPlan(id).pipe(finalize(() => this.busyUserIds.delete(id))).subscribe(() => {
      this.message = 'Plan przedluzono o 7 dni.';
      this.loadUsers();
    }, err => this.message = err.error && err.error.message ? err.error.message : 'Nie udalo sie przedluzyc planu.');
    this.userActionRequests.add(request);
  }
  requestDeleteCsv(row: { id: number; googleEmail: string }) {
    this.userToDelete = row;
    this.deleteCsvDialog.nativeElement.showModal();
  }
  cancelDeleteCsv() { this.deleteCsvDialog.nativeElement.close(); this.userToDelete = null; }
  confirmDeleteCsv() {
    if (!this.userToDelete) { return; }
    const id = this.userToDelete.id;
    this.cancelDeleteCsv();
    this.busyUserIds.add(id);
    const request = this.api.deleteUserCsvExports(id).pipe(finalize(() => this.busyUserIds.delete(id))).subscribe(result => {
      this.message = 'Usunieto eksporty CSV: ' + this.formatNumber(result.deleted) + '.';
    }, () => this.message = 'Nie udalo sie usunac eksportow CSV.');
    this.userActionRequests.add(request);
  }
  activeFilterTab = 'criteria';
  nipTerm = '';
  blockedByRodoFilter: boolean | null = null;
  cycleRodoFilter() {
    this.blockedByRodoFilter = this.blockedByRodoFilter === null ? true : this.blockedByRodoFilter === true ? false : null;
    this.updateFilters();
  }
  filters: CompanyFilters = { companyName: '', address: '', hasPhone: null, hasEmail: null, hasWebsite: null, recordLimit: 1000 };
  dateRangeEnabled = false;
  companyStatuses: AddressOption[] = [];
  companyStatusesError = '';
  private companyStatusesRequest: Subscription | null = null;

  loadCompanyStatuses() {
    if (this.companyStatusesRequest) { this.companyStatusesRequest.unsubscribe(); }
    this.companyStatusesError = '';
    this.companyStatusesRequest = this.api.companyStatuses().subscribe(statuses => this.companyStatuses = statuses,
      () => this.companyStatusesError = 'Nie udalo sie pobrac statusow dzialalnosci.');
  }

  toggleCompanyStatus(id: number, checked: boolean) {
    const selected = this.filters.companyStatusIds || [1];
    this.filters.companyStatusIds = (checked ? selected.concat(id) : selected.filter(value => value !== id))
      .filter((value, index, values) => values.indexOf(value) === index).sort((a, b) => a - b);
    this.updateFilters();
  }
  dateFrom = '2020-01-01';
  dateTo = new Date().toLocaleDateString('sv-SE');
  radiusEnabled = false;
  radiusKm = 1;
  radiusOptions = [1, 5, 10, 20, 50, 100, 200, 500];
  pkdDescription = '';
  classifyingPkd = false;
  filterError = '';
  addressFields: { level: AddressLevel; label: string; selectedId: number | null; term: string; options: AddressOption[]; loading: boolean; error: string; request: Subscription | null }[] = [
    { level: 'wojewodztwa', label: 'Województwo', selectedId: null, term: '', options: [], loading: false, error: '', request: null },
    { level: 'powiaty', label: 'Powiat', selectedId: null, term: '', options: [], loading: false, error: '', request: null },
    { level: 'gminy', label: 'Gmina', selectedId: null, term: '', options: [], loading: false, error: '', request: null },
    { level: 'miejscowosci', label: 'Miejscowość', selectedId: null, term: '', options: [], loading: false, error: '', request: null }
  ];
  private companiesRequest: Subscription | null = null;
  private filterDelay: Subscription | null = null;
  private classificationRequest: Subscription | null = null;

  updateFilters(delay = false) {
    if (this.filterDelay) { this.filterDelay.unsubscribe(); }
    if (delay) { this.filterDelay = timer(300).subscribe(() => this.updateFilters()); return; }
    this.filterError = '';
    const normalizedNip = this.nipTerm.replace(/[-\s]/g, '');
    if (!/^\d{0,10}$/.test(normalizedNip)) { this.filterError = 'NIP moze zawierac maksymalnie 10 cyfr.'; return; }
    if (this.dateRangeEnabled && (!/^\d{4}-\d{2}-\d{2}$/.test(this.dateFrom) || !/^\d{4}-\d{2}-\d{2}$/.test(this.dateTo) || this.dateTo < this.dateFrom)) {
      this.filterError = 'Podaj poprawne daty. Data do nie moze byc wczesniejsza niz data od.'; return;
    }
    this.filters.startedFrom = this.dateRangeEnabled ? this.dateFrom : undefined;
    this.filters.startedTo = this.dateRangeEnabled ? this.dateTo : undefined;
    ['wojewodztwoId', 'powiatId', 'gminaId', 'miejscowoscId'].forEach((key, i) => this.filters[key] = this.addressFields[i].selectedId || undefined);
    this.filters.radiusKm = this.radiusEnabled ? this.radiusKm : undefined;
    this.page = 1;
    this.loadCompanies();
  }
  cyclePresence(key: string) {
    this.filters[key] = this.filters[key] === null ? true : this.filters[key] === true ? false : null;
    this.updateFilters();
  }
  presenceLabel(value: boolean | null) { return value === null ? 'Dowolne' : value ? 'Jest' : 'Brak'; }
  visibleAddressOptions(index: number) {
    const field = this.addressFields[index];
    return field.options.filter(option => option.name.toLowerCase().includes(field.term.toLowerCase()));
  }
  loadAddressOptions(index: number) {
    const field = this.addressFields[index];
    if (field.request) { field.request.unsubscribe(); }
    field.loading = true; field.error = '';
    field.request = this.api.addressOptions(field.level, this.addressFields.slice(0, index).map(f => f.selectedId))
      .pipe(finalize(() => field.loading = false)).subscribe(options => field.options = options.sort((a, b) => a.name.localeCompare(b.name, 'pl')),
        () => field.error = 'Nie udalo sie pobrac lokalizacji.');
  }
  selectAddress(index: number, id: number | null) {
    this.addressFields[index].selectedId = id;
    this.radiusEnabled = false;
    this.addressFields.slice(index + 1).forEach(field => {
      if (field.request) { field.request.unsubscribe(); }
      field.selectedId = null; field.options = []; field.term = ''; field.error = ''; field.loading = false;
    });
    if (id && index < 3) { this.loadAddressOptions(index + 1); }
    this.updateFilters();
  }
  classifyPkd() {
    if (!this.pkdDescription.trim() || this.classifyingPkd) { return; }
    this.classifyingPkd = true;
    this.classificationRequest = this.api.classifyPkd(this.pkdDescription).pipe(finalize(() => this.classifyingPkd = false)).subscribe(result => {
      this.selectedPkds = this.sortPkds(result.pkdCodes.slice(0, 10));
      this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
      this.updatePkdSuggestions(); this.updateFilters();
    }, () => this.filterError = 'Nie udalo sie rozpoznac kodow PKD.');
  }
  ngOnDestroy() {
    if (this.companyStatusesRequest) { this.companyStatusesRequest.unsubscribe(); }
    this.userActionRequests.forEach(request => request.unsubscribe());
    this.rodoRequests.forEach(request => request.unsubscribe());
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    if (this.filterDelay) { this.filterDelay.unsubscribe(); }
    if (this.classificationRequest) { this.classificationRequest.unsubscribe(); }
    this.addressFields.forEach(field => { if (field.request) { field.request.unsubscribe(); } });
  }
  tab: 'companies' | 'users' = 'companies';
  companies: Company[] = [];
  users: AppUser[] = [];
  usersLoading = false;
  usersSortBy = 'displayName';
  usersDirection = 'asc';
  userColumns: SortableColumn[] = [
    { key: 'displayName', label: 'Imi\u0119 i nazwisko' }, { key: 'googleEmail', label: 'Email' },
    { key: 'planCode', label: 'Aktualny plan' }, { key: 'expiresAtUtc', label: 'Data wyga\u015bni\u0119cia' },
    { key: 'createdAtUtc', label: 'Login utworzony' }
  ];
  private dateUtc(value: string) { return /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z'; }
  get userRows() {
    const now = Date.now();
    const rows = this.users.map(user => {
      const plans = (user.plans || []).slice().sort((a, b) => new Date(this.dateUtc(b.expiresAtUtc)).getTime() - new Date(this.dateUtc(a.expiresAtUtc)).getTime());
      const plan = plans[0];
      const expiresAtUtc = plan ? this.dateUtc(plan.expiresAtUtc) : '';
      return { id: user.id, displayName: user.displayName || '', googleEmail: user.googleEmail,
        createdAtUtc: user.createdAtUtc ? this.dateUtc(user.createdAtUtc) : '',
        planCode: plan ? plan.planCode : '', descriptionHtml: plan ? plan.descriptionHtml || '' : '', expiresAtUtc, expired: !!plan && new Date(expiresAtUtc).getTime() <= now };
    });
    const email = this.userEmailFilter.trim().toLowerCase();
    return rows.filter(row => !email || row.googleEmail.toLowerCase().includes(email)).sort((a, b) => {
      let comparison = 0;
      if (this.usersSortBy === 'expiresAtUtc' || this.usersSortBy === 'createdAtUtc') {
        const first = a[this.usersSortBy], second = b[this.usersSortBy];
        if (!first || !second) { return first ? -1 : second ? 1 : a.id - b.id; }
        comparison = new Date(first).getTime() - new Date(second).getTime();
      } else {
        comparison = String(a[this.usersSortBy]).localeCompare(String(b[this.usersSortBy]), 'pl');
      }
      return (this.usersDirection === 'asc' ? comparison : -comparison) || a.id - b.id;
    });
  }
  userEmailFilter = '';
  trackUser(index: number, row: { id: number }) { return row.id; }
  planHint: { html: string; left: number; top: number } | null = null;
  showPlanHint(event: Event, html: string) {
    if (!html) { this.planHint = null; return; }
    const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.planHint = { html,
      left: Math.max(12, Math.min(bounds.left, window.innerWidth - Math.min(420, window.innerWidth - 24) - 12)),
      top: Math.max(12, Math.min(bounds.bottom + 8, window.innerHeight - Math.min(320, window.innerHeight - 24) - 12)) };
  }
  sortUsers(column: string) {
    if (this.usersLoading || !this.userColumns.some(c => c.key === column)) { return; }
    this.usersDirection = this.usersSortBy === column && this.usersDirection === 'asc' ? 'desc' : 'asc';
    this.usersSortBy = column;
  }
  businessTypes: BusinessType[] = [];
  allPkds: Pkd[] = [];
  pageSize = 10;
  page = 1;
  total = 0;
  loading = false;
  selectedCompanyId: number | null = null;
  detailsCompany: Company | null = null;
  rodoSavingIds = new Set<number>();
  private rodoRequests = new Set<Subscription>();
  toggleRodo(company: Company) {
    if (this.rodoSavingIds.has(company.id)) { return; }
    this.rodoSavingIds.add(company.id);
    const request = this.api.updateCompanyRodo(company.id, !company.blockedByRODO)
      .pipe(finalize(() => this.rodoSavingIds.delete(company.id))).subscribe(result => {
        company.blockedByRODO = result.blockedByRODO;
        const current = this.companies.find(c => c.id === company.id);
        if (current) { current.blockedByRODO = result.blockedByRODO; }
      }, () => this.message = 'Nie udalo sie zmienic ustawienia RODO.');
    this.rodoRequests.add(request);
  }
  formatNumber(value: number) { return new Intl.NumberFormat('pl-PL').format(value); }
  formatPhone(value: string | null | undefined) { return this.api.formatPhone(value); }
  emailUrl(email: string) { return this.api.emailUrl(email); }
  websiteHref(website: string) { return this.api.websiteHref(website); }
  fullAddress(company: Company) {
    const number = (company.buildingNumber || '') + (company.apartmentNumber ? '/' + company.apartmentNumber : '');
    return [company.city, company.postalCode, company.street, number].filter(Boolean).join(' ');
  }
  filter = '';
  selectedPkds: Pkd[] = [];
  pkdTerm = '';
  pkdSuggestions: Pkd[] = [];
  maxSelectedPkds = 10;
  foundPkdId: number | null = null;
  selectedPkdId: number | null = null;
  sortBy = 'startedAt';
  direction = 'desc';
  message = '';
  planCode = 'PX5';

  companyColumns: SortableColumn[] = [
    { key: 'name', label: 'Nazwa' }, { key: 'nip', label: 'NIP' },
    { key: 'fullAddress', label: 'Adres' }, { key: 'phone', label: 'Telefon' },
    { key: 'email', label: 'Email' }, { key: 'website', label: 'Witryna WWW' },
    { key: 'startedAt', label: 'Data aktywno\u015bci' }
  ];

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadCompanyStatuses();
    this.loadPkds();
    this.loadAddressOptions(0);
    this.loadCompanies();
    this.loadUsers();
  }

  loadCompanies() {
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    this.loading = true;
    this.companiesRequest = this.api.adminCompanies(this.page, this.sortBy, this.direction, '', this.selectedPkds.map(p => p.id), this.filters, this.nipTerm.replace(/[-\s]/g, ''), this.blockedByRodoFilter).pipe(finalize(() => this.loading = false)).subscribe(x => {
      this.companies = x.items;
      this.total = x.total;
      this.pageSize = x.pageSize;
    }, err => this.message = err.error && err.error.message ? err.error.message : 'Nie udalo sie pobrac firm.');
  }

  loadUsers() {
    this.usersLoading = true;
    this.api.users().pipe(finalize(() => this.usersLoading = false)).subscribe(x => this.users = x,
      () => this.message = 'Nie udalo sie pobrac uzytkownikow.');
  }

  loadBusinessTypes() { this.api.businessTypes().subscribe(x => this.businessTypes = x); }

  loadPkds() {
    this.api.pkd().subscribe(x => {
      this.allPkds = this.sortPkds(x);
      this.updatePkdSuggestions();
    });
  }

  findPkd() {
    this.updatePkdSuggestions();
  }

  addSelectedFoundPkd() {
    if (this.selectedPkds.length >= this.maxSelectedPkds) { return; }

    const pkd = this.pkdSuggestions.find(x => x.id === Number(this.foundPkdId));
    if (!pkd || this.selectedPkds.some(x => x.id === pkd.id)) { return; }

    this.selectedPkds = this.sortPkds(this.selectedPkds.concat(pkd));
    this.selectedPkdId = pkd.id;
    this.pkdSuggestions = this.pkdSuggestions.filter(x => x.id !== pkd.id);
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
    this.page = 1;
    this.loadCompanies();
  }

  removeSelectedPkd() {
    this.selectedPkds = this.selectedPkds.filter(x => x.id !== Number(this.selectedPkdId));
    this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  clearPkds() {
    if (!this.selectedPkds.length) { return; }
    this.selectedPkds = [];
    this.selectedPkdId = null;
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  sort(column: string) {
    if (this.loading) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.page = 1;
    this.loadCompanies();
  }

  grant(user: AppUser) {
    this.api.grantPlan(user.id, this.planCode).subscribe(() => {
      this.message = 'Plan zostal przyznany i wyslano email albo wpisano log, jesli SMTP nie jest skonfigurowane.';
      this.loadUsers();
    });
  }

  totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  next() { if (!this.loading && this.page < this.totalPages()) { this.page++; this.loadCompanies(); } }
  prev() { if (!this.loading && this.page > 1) { this.page--; this.loadCompanies(); } }
  last() { this.page = this.totalPages(); this.loadCompanies(); }

  private sortPkds(pkds: Pkd[]) {
    return pkds.slice().sort((a, b) => this.pkdLabel(a).localeCompare(this.pkdLabel(b)));
  }

  private updatePkdSuggestions() {
    const term = this.pkdTerm.trim().toLowerCase();
    this.pkdSuggestions = this.allPkds.filter(pkd => {
      const isAlreadySelected = this.selectedPkds.some(selected => selected.id === pkd.id);
      return !isAlreadySelected && (!term || this.pkdLabel(pkd).toLowerCase().indexOf(term) >= 0);
    });
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
  }

  pkdLabel(pkd: Pkd) {
    return `${pkd.code} ${pkd.name}`;
  }
}
