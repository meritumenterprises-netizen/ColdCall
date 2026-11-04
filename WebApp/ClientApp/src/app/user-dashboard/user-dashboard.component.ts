import { Component, ElementRef, HostListener, Input, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { SearchDraftStore } from '../services/search-draft-store.service';
import { isFutureActivityDate } from '../services/activity-date';
import { from, Observable, of, Subscription, timer } from 'rxjs';
import { catchError, concatMap, finalize, map, switchMap, take, timeout } from 'rxjs/operators';
import { AddressLevel, AddressOption, ApiService, CompanyFilters, CompanyShort, CsvExportRow, Pkd, SearchCriteria, SearchProfile, SortableColumn } from '../services/api.service';

interface ResultTab {
  key: string;
  label: string;
  days: number | undefined;
  page: number;
}

@Component({
  selector: 'app-user-dashboard',
  templateUrl: './user-dashboard.component.html',
  styleUrls: ['./user-dashboard.component.css']
})
export class UserDashboardComponent implements OnInit, OnDestroy {
  isFutureActivityDate = isFutureActivityDate;
  searches: SearchProfile[] = [];
  searchRecordCounts: { [id: number]: number | null } = {};
  private searchCountsRequest: Subscription | null = null;
  private searchesLoaded = false;
  @ViewChild('searchNameInput', { static: true }) searchNameInput: ElementRef;
  allPkds: Pkd[] = [];
  selectedPkds: Pkd[] = [];
  pkdTerm = '';
  pkdDescription = '';
  classifyingPkd = false;
  @ViewChild('pkdErrorDialog', { static: true }) pkdErrorDialog: ElementRef;
  @ViewChild('duplicateExportDialog', { static: true }) duplicateExportDialog: ElementRef;
  @ViewChild('duplicateSearchDialog', { static: true }) duplicateSearchDialog: ElementRef;
  pkdClassificationMessage = '';
  pkdClassificationError = '';
  private pkdClassificationRequest: Subscription | null = null;
  restoringSearch = false;
  private searchesRequest: Subscription | null = null;
  private companyStatusesRequest: Subscription | null = null;
  companyStatuses: { id: number; name: string }[] = [];
  companyStatusesError = '';
  private deleteSearchRequest: Subscription | null = null;
  private restoreSearchRequest: Subscription | null = null;
  activeDashboardTab: 'search' | 'export' = 'search';
  csvExports: CsvExportRow[] = [];
  exportsLoading = false;
  exportsError = '';
  exportsSortBy: 'csvFileName' | 'createdOn' | 'numberOfRecords' = 'createdOn';
  exportsDirection: 'asc' | 'desc' = 'desc';
  downloadingExportId: number | null = null;
  deletingExportId: number | null = null;
  @ViewChild('deleteCsvDialog', { static: true }) deleteCsvDialog: ElementRef;
  private pendingCsvDeletion: CsvExportRow | null = null;
  private deleteExportRequest: Subscription | null = null;
  private exportsRequest: Subscription | null = null;
  private downloadRequest: Subscription | null = null;
  activeFilterTab: 'pkd' | 'criteria' | 'contact' | 'address' = 'criteria';
  readonly resultTabs: ResultTab[] = [
    { key: 'all', label: 'Wszystkie', days: undefined, page: 1 },
    { key: '7', label: 'Zmienione w ost. 7 dniach', days: 7, page: 1 },
    { key: '3', label: 'Zmienione w ost. 3 dniach', days: 3, page: 1 },
    { key: 'notes', label: 'Notatki', days: undefined, page: 1 }
  ];
  hasNotes = false;
  get visibleResultTabs() { return this.resultTabs.filter(tab => tab.key !== 'notes' || this.hasNotes); }
  activeResultTab = this.resultTabs[0];
  private allResultsCountRequest: Subscription | null = null;
  addressFields: { level: AddressLevel; label: string; options: AddressOption[]; visible: AddressOption[];
    selectedId: number | null; term: string; loading: boolean; error: string; request: Subscription | null }[] =
    [
      { level: 'wojewodztwa', label: 'Wojew\u00f3dztwo' },
      { level: 'powiaty', label: 'Powiat' },
      { level: 'gminy', label: 'Gmina' },
      { level: 'miejscowosci', label: 'Miejscowo\u015bci' }
    ].map(field => ({ ...field, level: field.level as AddressLevel, options: [], visible: [], selectedId: null,
      term: '', loading: false, error: '', request: null }));
  radiusEnabled = false;
  radiusKm = 1;
  private addressFiltersChanged = false;
  maxRecordLimit = 0;
  readonly radiusOptions = [1, 5, 10, 20, 50, 100, 200, 500];
  pkdSuggestions: Pkd[] = [];
  maxSelectedPkds = 10;
  foundPkdId: number | null = null;
  selectedPkdId: number | null = null;
  pkdHint: { text: string; left: number; top: number | null; bottom: number | null } | null = null;
  companies: CompanyShort[] = [];
  selectedCompanyId: number | null = null;
  detailsCompany: CompanyShort | null = null;
  detailsActiveTab: 'description' | 'notes' = 'description';
  total = 0;
  totalWithoutLimit = 0;
  readonly pageSize = 10;
  readonly loadingRows = Array.from({ length: 10 }, (_, index) => index);
  loading = false;
  private refreshingSearchCount = false;
  private companiesRequest: Subscription | null = null;
  page = 1;
  sortBy = 'dataAktywnosci';
  direction = 'desc';
  searchName = '';
  currentSearchId: number | null = null;
  private nextDraftId = -1;
  private searchDraftUserId = 0;
  savingSearch = false;
  searchSaved = false;

  get searchPlanExpired() {
    const user = this.api.user();
    if (!user || !user.hasActivePlan) { return true; }
    if (!user.plan || !user.plan.expiresAtUtc) { return false; }
    const expiration = user.plan.expiresAtUtc;
    const utcExpiration = /(?:Z|[+-]\d{2}:\d{2})$/i.test(expiration) ? expiration : expiration + 'Z';
    return new Date(utcExpiration).getTime() <= Date.now();
  }
  get filtersLocked() { return this.searchPlanExpired || this.classifyingPkd || this.restoringSearch || this.savingSearch || this.exportingCsv; }
  private savedSearchState: string | null = null;
  private saveSearchRequest: Subscription | null = null;
  private companyNameDelay: Subscription | null = null;
  error = '';
  filterError = '';
  filterDraft: CompanyFilters = { companyName: '', address: '', hasPhone: null, hasEmail: null, hasWebsite: null, recordLimit: 1000, companyStatusIds: [1] };
  appliedFilters: CompanyFilters = { ...this.filterDraft };
  private lastUpdatedFilterState: string | null = null;
  dateRangeEnabled = false;
  dateFrom = '2020-01-01';
  dateTo = this.todayDate();
  private readonly numberFormatter = new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 0 });
  private readonly addressCollator = new Intl.Collator('pl', { sensitivity: 'base' });

  columns: SortableColumn[] = [
    { key: 'name', label: 'Nazwa' },
    { key: 'fullAddress', label: 'Adres' },
    { key: 'phone', label: 'Telefon' },
    { key: 'email', label: 'Email' },
    { key: 'www', label: 'Witryna WWW' },
    { key: 'dataAktywnosci', label: 'Dzia\u0142alno\u015b\u0107 od' },
    { key: 'dataModifiedOn', label: 'Zmodyfikowano' },
    { key: 'details', label: 'Szczeg\u00f3\u0142y' }
  ];

  @Input() publicPageTitle = '';
  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router, private searchDraftStore: SearchDraftStore) {}

  ngOnInit() {
    const user = this.api.user();
    this.searchDraftUserId = user ? user.id : 0;
    this.maxRecordLimit = user && Number.isInteger(user.maxNumberOfExportedRecords)
      ? Math.max(0, user.maxNumberOfExportedRecords) : 0;
    this.filterDraft.recordLimit = this.maxRecordLimit;
    this.appliedFilters = { ...this.filterDraft };
    this.lastUpdatedFilterState = this.filterState();
    this.loadSearches();
    if (this.publicPageTitle) { return; }
    this.loadCompanyStatuses();
    this.loadPkds();
  }

  loadCompanyStatuses() {
    if (this.companyStatusesRequest) { this.companyStatusesRequest.unsubscribe(); }
    this.companyStatusesError = '';
    this.companyStatusesRequest = this.api.companyStatuses().subscribe(statuses => {
      this.companyStatuses = statuses;
    }, () => this.companyStatusesError = 'Nie udalo sie pobrac statusow dzialalnosci.');
  }

  toggleCompanyStatus(id: number, checked: boolean) {
    if (this.filtersLocked || this.searchPlanExpired) { return; }
    const selected = this.filterDraft.companyStatusIds || [1];
    this.filterDraft.companyStatusIds = (checked ? selected.concat(id) : selected.filter(value => value !== id))
      .filter((value, index, values) => values.indexOf(value) === index).sort((a, b) => a - b);
    this.updateFilters();
  }

  loadSearches() {
    this.restoringSearch = true;
    this.searchesRequest = this.api.searches().subscribe(profiles => {
      this.searches = profiles;
      profiles.forEach(profile => {
        if (typeof profile.recordCount === 'number') { this.searchRecordCounts[profile.id] = profile.recordCount; }
      });
      this.searchesLoaded = true;
      if (this.publicPageTitle) {
        this.restoringSearch = false;
        this.refreshSearchCounts(this.searches);
        return;
      }
      const retained = this.searchDraftStore.load(this.searchDraftUserId);
      this.searches = profiles.concat(retained.drafts);
      retained.drafts.forEach(profile => {
        if (typeof profile.recordCount === 'number') { this.searchRecordCounts[profile.id] = profile.recordCount; }
      });
      this.nextDraftId = Math.min(-1, ...retained.drafts.map(draft => draft.id - 1));
      const requested = this.route.snapshot.queryParams.searchId == null
        ? retained.selectedSearchId : Number(this.route.snapshot.queryParams.searchId);
      if (this.route.snapshot.queryParams.newSearch === 'true') { this.restoringSearch = false; this.newSearch(); }
      else if (this.searches.length) {
        this.selectSearch(this.searches.find(profile => profile.id === requested) || this.searches[0]);
        if (this.route.snapshot.queryParams.view === 'csv') { this.selectDashboardTab('export'); }
      }
      else { this.restoringSearch = false; this.loadAddressOptions(0); this.loadCompanies(); }
      this.refreshSearchCounts(this.searches);
    }, () => {
      this.restoringSearch = false;
      if (this.publicPageTitle) { this.filterError = 'Nie udalo sie pobrac zapisanych wyszukiwan.'; return; }
      this.loadAddressOptions(0);
      this.loadCompanies();
      this.filterError = 'Nie udalo sie pobrac zapisanych wyszukiwan.';
    });
  }

  deleteSearch(search: SearchProfile) {
    if (!this.canDeleteSearches) { return; }
    if (this.savingSearch || this.restoringSearch || this.classifyingPkd || this.exportingCsv) { return; }
    if (!window.confirm('Usunac wyszukiwanie "' + this.sidebarSearchName(search) + '" oraz wszystkie jego eksporty CSV?')) { return; }
    const remove = () => {
      this.savingSearch = false;
      this.searches = this.searches.filter(item => item.id !== search.id);
      delete this.searchRecordCounts[search.id];
      if (this.currentSearchId !== search.id) { return; }
      this.currentSearchId = null;
      this.activeDashboardTab = 'search';
      this.csvExports = []; this.searchName = ''; this.companies = []; this.total = 0; this.totalWithoutLimit = 0;
      this.selectedCompanyId = null; this.detailsCompany = null;
      if (this.searches.length) { this.selectSearch(this.searches[0]); }
      else { this.newSearch(); }
    };
    if (search.id <= 0) { remove(); return; }
    this.error = '';
    this.savingSearch = true;
    this.deleteSearchRequest = this.api.deleteSearch(search.id).pipe(finalize(() => this.savingSearch = false))
      .subscribe(remove, err => {
        this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie usunac wyszukiwania.';
        if (this.activeDashboardTab === 'export') { this.exportsError = this.error; }
      });
  }

  sidebarSearchName(search: SearchProfile): string {
    return (this.currentSearchId === search.id ? this.searchName : search.name) || 'Nowe wyszukiwanie';
  }

  shortSidebarSearchName(search: SearchProfile): string {
    const name = this.sidebarSearchName(search);
    return name.length > 64 ? name.slice(0, 61) + '...' : name;
  }

  searchRecordLabel(search: SearchProfile) {
    if (search.id === this.currentSearchId && ((this.activeResultTab.key === 'all' && this.refreshingSearchCount) || this.restoringSearch)) { return '... rekord\u00f3w'; }
    const count = this.searchRecordCounts[search.id];
    return typeof count === 'number' ? this.formatNumber(count) + ' rekord\u00f3w'
      : count === null ? '- rekord\u00f3w' : '... rekord\u00f3w';
  }

  private rememberRecordCount(id: number, count: number) {
    this.searchRecordCounts[id] = count;
    const profile = this.searches.find(search => search.id === id);
    if (profile) { profile.recordCount = count; }
    if (id < 0) { this.searchDraftStore.save(this.searchDraftUserId, this.searches, this.currentSearchId); }
  }

  private refreshSearchCounts(profiles: SearchProfile[]) {
    if (this.searchCountsRequest) { this.searchCountsRequest.unsubscribe(); }
    // Sequential background requests avoid blocking the first grid or flooding SQL.
    const pendingFirst = profiles.slice().sort((a, b) =>
      Number(typeof this.searchRecordCounts[a.id] === 'number') - Number(typeof this.searchRecordCounts[b.id] === 'number'));
    this.searchCountsRequest = from(pendingFirst).pipe(concatMap(profile => {
      if (profile.id === this.currentSearchId) { return of(undefined); }
      const criteria = profile.criteria;
      const filters: CompanyFilters = {
        companyName: criteria && criteria.companyName || '', address: '', recordLimit: this.maxRecordLimit,
        hasPhone: criteria && criteria.hasPhone != null ? criteria.hasPhone : null,
        hasEmail: criteria && criteria.hasEmail != null ? criteria.hasEmail : null,
        hasWebsite: criteria && criteria.hasWebsite != null ? criteria.hasWebsite : null,
        companyStatusIds: criteria && criteria.companyStatusIds ? criteria.companyStatusIds.slice() : [1],
        startedFrom: criteria && criteria.dateRangeEnabled ? criteria.dateFrom || undefined : undefined,
        startedTo: criteria && criteria.dateRangeEnabled ? criteria.dateTo || undefined : undefined,
        wojewodztwoId: criteria && criteria.wojewodztwoId || undefined,
        powiatId: criteria && criteria.powiatId || undefined,
        gminaId: criteria && criteria.gminaId || undefined,
        miejscowoscId: criteria && criteria.miejscowoscId || undefined,
        radiusKm: criteria && criteria.hasFirmyWPromieniu ? criteria.firmyWPromieniu || 1 : undefined
      };
      return this.api.companies(1, criteria && criteria.sortColumn || 'dataAktywnosci',
        criteria && criteria.sortDirection || 'desc', profile.pkdCodes.map(pkd => pkd.id), filters).pipe(
        map(result => {
          if (profile.id !== this.currentSearchId) { this.rememberRecordCount(profile.id, result.total); }
        }),
        catchError(() => {
          if (profile.id !== this.currentSearchId) { this.searchRecordCounts[profile.id] = null; }
          return of(undefined);
        }));
    })).subscribe();
  }

  get canAddSearch() {
    const user = this.api.user();
    return this.searchesLoaded && !!user && user.hasActivePlan && this.searches.length < user.maxNumberOfSearches;
  }

  get addSearchHint() {
    const user = this.api.user();
    const remaining = user && user.hasActivePlan ? Math.max(0, user.maxNumberOfSearches - this.searches.length) : 0;
    return 'Dodaj nowe wyszukiwanie. Pozostała liczba dodatkowych wyszukiwań w aktualnym planie: ' + remaining;
  }

  private get canPersistNewSearch() {
    const user = this.api.user();
    return this.searchesLoaded && !!user && user.hasActivePlan
      && this.searches.filter(search => search.id > 0).length < user.maxNumberOfSearches;
  }

  private rememberDraft() {
    if (this.currentSearchId === null || this.currentSearchId > 0) { return; }
    const index = this.searches.findIndex(search => search.id === this.currentSearchId);
    if (index >= 0) {
      this.searches[index] = { id: this.currentSearchId, name: this.searchName,
        pkdCodes: this.selectedPkds.slice(), criteria: this.currentCriteria(),
        recordCount: this.searchRecordCounts[this.currentSearchId] == null ? this.searches[index].recordCount : this.searchRecordCounts[this.currentSearchId] };
    }
  }

  newSearch() {
    if (this.publicPageTitle) { this.router.navigate(['/dashboard'], { queryParams: { newSearch: 'true' } }); return; }
    if (!this.canAddSearch || this.savingSearch || this.restoringSearch || this.classifyingPkd) { return; }
    this.rememberDraft();
    this.activeDashboardTab = 'search';
    if (this.companyNameDelay) { this.companyNameDelay.unsubscribe(); }
    if (this.restoreSearchRequest) { this.restoreSearchRequest.unsubscribe(); }
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    this.addressFields.forEach(field => {
      if (field.request) { field.request.unsubscribe(); field.request = null; }
      field.options = []; field.visible = []; field.selectedId = null; field.term = '';
      field.loading = false; field.error = '';
    });
    this.currentSearchId = this.nextDraftId--;
    this.loadCsvExports();
    this.searchName = 'Nowe wyszukiwanie';
    let suffix = 2;
    while (this.searches.some(search => search.name.trim().toLowerCase() === this.searchName.toLowerCase())) {
      this.searchName = 'Nowe wyszukiwanie (' + suffix++ + ')';
    }
    this.searchSaved = false;
    this.savedSearchState = null;
    this.selectedPkds = [];
    this.selectedPkdId = null;
    this.pkdTerm = '';
    this.pkdDescription = '';
    this.pkdHint = null;
    this.pkdClassificationError = '';
    this.filterDraft = { companyName: '', address: '', hasPhone: null, hasEmail: null,
      hasWebsite: null, recordLimit: this.maxRecordLimit, companyStatusIds: [1] };
    this.appliedFilters = { ...this.filterDraft };
    this.dateRangeEnabled = false; this.dateFrom = '2020-01-01'; this.dateTo = this.todayDate();
    this.radiusEnabled = false; this.radiusKm = 1;
    this.sortBy = 'dataAktywnosci'; this.direction = 'desc';
    this.activeFilterTab = 'criteria';
    this.hasNotes = false;
    this.activeResultTab = this.resultTabs[0];
    this.error = ''; this.filterError = '';
    this.selectedCompanyId = null; this.detailsCompany = null;
    this.addressFiltersChanged = false;
    this.lastUpdatedFilterState = this.filterState();
    this.resultTabs.forEach(tab => tab.page = 1);
    this.page = 1;
    this.searches.unshift({ id: this.currentSearchId, name: this.searchName,
      pkdCodes: [], criteria: this.currentCriteria() });
    this.updatePkdSuggestions();
    this.loadAddressOptions(0);
    this.loadCompanies();
    this.refreshSearchCounts(this.searches);
    this.searchNameInput.nativeElement.focus();
  }

  selectDashboardTab(tab: 'search' | 'export') {
    if (tab === 'export' && this.searchPlanExpired) { return; }
    this.activeDashboardTab = tab;
    this.loadCsvExports();
  }

  openSearchExports(profile: SearchProfile) {
    if (this.publicPageTitle) { this.router.navigate(['/dashboard'], { queryParams: { searchId: profile.id, view: 'csv' } }); return; }
    if (profile.id <= 0 || this.searchPlanExpired || this.savingSearch || this.restoringSearch || this.classifyingPkd || this.exportingCsv) { return; }
    if (profile.id !== this.currentSearchId) { this.selectSearch(profile); }
    if (profile.id === this.currentSearchId) { this.selectDashboardTab('export'); }
  }

  loadCsvExports(refreshQuota = true) {
    if (refreshQuota) { this.loadCsvExportQuota(); }
    if (this.exportsRequest) { this.exportsRequest.unsubscribe(); }
    this.csvExports = [];
    this.exportsError = '';
    if (this.activeDashboardTab !== 'export' || !this.api.isAuthenticated() || this.currentSearchId === null || this.currentSearchId <= 0) {
      this.exportsLoading = false;
      return;
    }
    this.loadCsvDeletionQuota();
    this.exportsLoading = true;
    this.exportsRequest = this.api.csvExports(this.currentSearchId, this.exportsSortBy, this.exportsDirection)
      .pipe(timeout(15000), take(1), finalize(() => this.exportsLoading = false)).subscribe(rows => {
        this.exportsLoading = false;
        this.csvExports = rows;
        const profile = this.searches.find(search => search.id === this.currentSearchId);
        if (profile) { profile.hasCsvExports = rows.length > 0; }
      }, err => {
        this.exportsLoading = false;
        this.exportsError = err.name === 'TimeoutError'
          ? 'Przekroczono czas oczekiwania na eksporty CSV. Sprobuj ponownie.'
          : 'Nie udalo sie pobrac eksportow CSV.';
      });
  }

  sortCsvExports(column: 'csvFileName' | 'createdOn' | 'numberOfRecords') {
    if (this.exportsLoading) { return; }
    this.exportsDirection = this.exportsSortBy === column && this.exportsDirection === 'asc' ? 'desc' : 'asc';
    this.exportsSortBy = column;
    this.loadCsvExports();
  }

  downloadCsvExport(event: Event, row: CsvExportRow) {
    event.preventDefault();
    if (this.searchPlanExpired) { return; }
    if (!this.api.isAuthenticated() || this.downloadingExportId !== null || this.deletingExportId !== null) { return; }
    this.downloadingExportId = row.id;
    this.exportsError = '';
    this.downloadRequest = this.api.downloadCsv(row).pipe(finalize(() => this.downloadingExportId = null))
      .subscribe(blob => this.saveCsvFile(blob, row.csvFileName),
        err => this.exportsError = err.status === 404 ? 'Nie znaleziono pliku CSV.' : 'Nie udało się pobrać pliku CSV.');
  }

  csvDeletionsRemaining: number | null = null;
  csvDeletionLimit: number | null = null;
  csvDeletionQuotaLoading = false;
  csvDeletionQuotaError = false;
  private deletionQuotaRequest: Subscription | null = null;

  loadCsvDeletionQuota() {
    if (this.deletionQuotaRequest) { this.deletionQuotaRequest.unsubscribe(); }
    this.csvDeletionQuotaLoading = true; this.csvDeletionQuotaError = false;
    this.deletionQuotaRequest = this.api.csvDeletionQuota().pipe(timeout(15000),
      finalize(() => this.csvDeletionQuotaLoading = false)).subscribe(quota => {
        this.csvDeletionLimit = quota.limit; this.csvDeletionsRemaining = quota.remaining;
      }, () => this.csvDeletionQuotaError = true);
  }
  get csvDeletionLimitReached() { return this.csvDeletionLimit !== null && this.csvDeletionsRemaining === 0; }

  get canDeleteSearches() {
    const user = this.api.user();
    return !!user && (user.isAdmin || !user.plan || user.plan.code.toUpperCase() !== 'PX0');
  }

  get canDeleteCsvExports() {
    const user = this.api.user();
    return !!(user && user.hasActivePlan && user.plan && user.plan.enableCSVDeleting === true && !this.searchPlanExpired);
  }

  deleteCsvExport(row: CsvExportRow) {
    if (!this.canDeleteCsvExports || this.searchPlanExpired || !this.api.isAuthenticated() || this.exportsLoading
        || this.deletingExportId !== null || this.downloadingExportId !== null) { return; }
    this.pendingCsvDeletion = row;
    this.deleteCsvDialog.nativeElement.showModal();
  }

  cancelCsvDeletion() {
    this.pendingCsvDeletion = null;
    this.deleteCsvDialog.nativeElement.close();
  }

  confirmCsvDeletion() {
    const row = this.pendingCsvDeletion;
    this.cancelCsvDeletion();
    if (row) { this.performCsvDeletion(row); }
  }

  private performCsvDeletion(row: CsvExportRow) {
    if (!this.canDeleteCsvExports || this.searchPlanExpired || !this.api.isAuthenticated() || this.exportsLoading
        || this.deletingExportId !== null || this.downloadingExportId !== null) { return; }
    const hasOtherExports = this.csvExports.some(exportRow => exportRow.id !== row.id);
    this.deletingExportId = row.id;
    this.exportsError = '';
    this.deleteExportRequest = this.api.deleteCsvExport(row.id)
      .pipe(take(1), finalize(() => this.deletingExportId = null)).subscribe(() => {
        const profile = this.searches.find(search => search.id === row.searchProfileId);
        if (profile) { profile.hasCsvExports = hasOtherExports; }
        if (this.currentSearchId === row.searchProfileId) { this.loadCsvExports(); }
      }, err => {
        this.exportsError = err.error && err.error.message ? err.error.message
          : err.status === 404 ? 'Nie znaleziono pliku CSV.' : 'Nie udalo sie usunac pliku CSV.';
        if (err.status === 429) { this.loadCsvDeletionQuota(); }
      });
  }

  private saveCsvFile(blob: Blob, name: string) {
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    window.setTimeout(() => window.URL.revokeObjectURL(url), 1000);
  }

  selectFilterTab(tab: 'pkd' | 'criteria' | 'contact' | 'address') {
    this.activeFilterTab = tab;
    this.pkdHint = null;
  }

  navigateFilterTabs(event: KeyboardEvent) {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].indexOf(event.key) === -1) { return; }
    event.preventDefault();
    const tabs: ('pkd' | 'criteria' | 'contact' | 'address')[] = ['criteria', 'pkd', 'address', 'contact'];
    const index = tabs.indexOf(this.activeFilterTab);
    const tab = event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1] :
      tabs[(index + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
    this.selectFilterTab(tab);
    const buttons = (event.currentTarget as HTMLElement).parentElement.querySelectorAll<HTMLButtonElement>('[role="tab"]');
    buttons[tabs.indexOf(tab)].focus();
  }

  addressFieldDisabled(index: number) {
    return this.addressFields.slice(0, index).some(field => field.selectedId === null);
  }

  get radiusAvailable() {
    return this.addressFields.every(field => field.selectedId != null && field.selectedId > 0);
  }

  loadAddressOptions(index: number) {
    if (this.addressFieldDisabled(index)) { return; }
    const field = this.addressFields[index];
    if (field.request) { field.request.unsubscribe(); }
    field.loading = true;
    field.error = '';
    const parents = this.addressFields.slice(0, index).map(parent => parent.selectedId);
    field.request = this.api.addressOptions(field.level, parents).pipe(finalize(() => field.loading = false))
      .subscribe(options => {
        field.options = options.slice()
          .sort((a, b) => this.addressCollator.compare(a.name, b.name) || a.id - b.id);
        this.filterAddressOptions(index);
      }, () => field.error = 'Nie udalo sie pobrac listy.');
  }

  filterAddressOptions(index: number) {
    const field = this.addressFields[index];
    const term = field.term.trim().toLowerCase();
    field.visible = field.options.filter(option => option.name.toLowerCase().includes(term));
    if (field.selectedId !== null && !field.visible.some(option => option.id === field.selectedId)) {
      this.selectAddressOption(index, null);
    }
  }

  selectAddressOption(index: number, id: number | null, refresh = true) {
    const field = this.addressFields[index];
    if (this.addressFieldDisabled(index)) { return; }
    const selectedId = field.visible.some(option => option.id === id) ? id : null;
    if (field.selectedId === selectedId) { return; }
    field.selectedId = selectedId;
    this.addressFiltersChanged = true;
    // Cancel dependent requests before clearing their selections and lists.
    this.addressFields.slice(index + 1).forEach(child => {
      if (child.request) { child.request.unsubscribe(); child.request = null; }
      child.options = []; child.visible = []; child.selectedId = null; child.term = ''; child.error = ''; child.loading = false;
    });
    if (!this.radiusAvailable) { this.radiusEnabled = false; }
    if (field.selectedId !== null && index < this.addressFields.length - 1) { this.loadAddressOptions(index + 1); }
    if (refresh) { this.updateFilters(); }
  }

  setRadiusEnabled(enabled: boolean) {
    enabled = enabled && this.radiusAvailable;
    if (this.radiusEnabled === enabled) { return; }
    this.radiusEnabled = enabled;
    this.addressFiltersChanged = true;
    this.updateFilters();
  }

  setRadiusKm(km: number) {
    if (this.radiusKm === km) { return; }
    this.radiusKm = km;
    this.addressFiltersChanged = true;
    this.updateFilters();
  }

  loadPkds() {
    this.api.pkd().subscribe(x => {
      this.allPkds = this.sortPkds(x);
      this.updatePkdSuggestions();
    });
  }

  selectResultTab(tab: ResultTab) {
    if (tab.key === 'notes' && !this.hasNotes) { return; }
    if (tab === this.activeResultTab) { return; }
    this.activeResultTab.page = this.page;
    this.activeResultTab = tab;
    this.page = tab.page;
    this.selectedCompanyId = null;
    this.detailsCompany = null;
    this.loadCompanies(false);
  }

  navigateResultTabs(event: KeyboardEvent) {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].indexOf(event.key) < 0) { return; }
    event.preventDefault();
    const tabs = this.visibleResultTabs;
    const index = tabs.indexOf(this.activeResultTab);
    const count = tabs.length;
    const tab = event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[count - 1] :
      tabs[(index + (event.key === 'ArrowRight' ? 1 : count - 1)) % count];
    this.selectResultTab(tab);
    const buttons = (event.currentTarget as HTMLElement).parentElement.querySelectorAll<HTMLButtonElement>('[role="tab"]');
    buttons[tabs.indexOf(tab)].focus();
  }

  loadCompanies(refreshSearchCount = true) {
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    if (refreshSearchCount && this.allResultsCountRequest) { this.allResultsCountRequest.unsubscribe(); }
    this.error = '';
    this.loading = true;
    this.refreshingSearchCount = refreshSearchCount;
    if (refreshSearchCount && this.activeResultTab.key !== 'all' && this.currentSearchId !== null) {
      this.allResultsCountRequest = this.api.companies(1, this.sortBy, this.direction,
        this.selectedPkds.map(x => x.id), this.appliedFilters).subscribe(
        result => { if (this.currentSearchId !== null) { this.rememberRecordCount(this.currentSearchId, result.total); } },
        () => { if (this.currentSearchId !== null) { this.searchRecordCounts[this.currentSearchId] = null; } });
    }
    this.companiesRequest = this.api.companies(this.page, this.sortBy, this.direction, this.selectedPkds.map(x => x.id), this.appliedFilters, this.activeResultTab.days, this.activeResultTab.key === 'notes')
      .pipe(finalize(() => { this.loading = false; this.refreshingSearchCount = false; }))
      .subscribe(result => {
      this.hasNotes = result.hasNotes === true;
      if (this.activeResultTab.key === 'notes' && !this.hasNotes) {
        this.activeResultTab = this.resultTabs[0];
        this.page = 1;
        Promise.resolve().then(() => this.loadCompanies(false));
        return;
      }
      this.companies = result.items;
      if (!this.companies.some(row => row.id === this.selectedCompanyId)) { this.selectedCompanyId = null; }
      this.total = result.total;
      this.totalWithoutLimit = result.totalWithoutLimit == null ? result.total : result.totalWithoutLimit;
      if (refreshSearchCount && this.activeResultTab.key === 'all' && this.currentSearchId !== null) { this.rememberRecordCount(this.currentSearchId, result.total); }
      this.page = result.page;
      this.activeResultTab.page = this.page;
    }, err => {
      this.companies = [];
      this.total = 0;
      this.totalWithoutLimit = 0;
      if (refreshSearchCount && this.activeResultTab.key === 'all' && this.currentSearchId !== null) { this.searchRecordCounts[this.currentSearchId] = null; }
      this.error = err.status === 402 ? '' :
        err.status === 400 && err.error && err.error.message ? err.error.message : 'Nie udalo sie pobrac firm.';
    });
  }

  @HostListener('window:beforeunload')
  preserveSearchDrafts() {
    if (this.publicPageTitle || !this.searchesLoaded) { return; }
    this.rememberDraft();
    this.searchDraftStore.save(this.searchDraftUserId, this.searches, this.currentSearchId);
  }

  ngOnDestroy() {
    this.preserveSearchDrafts();
    if (this.deletionQuotaRequest) { this.deletionQuotaRequest.unsubscribe(); }
    this.cancelCsvDeletion();
    if (this.deleteExportRequest) { this.deleteExportRequest.unsubscribe(); }
    if (this.companyStatusesRequest) { this.companyStatusesRequest.unsubscribe(); }
    if (this.deleteSearchRequest) { this.deleteSearchRequest.unsubscribe(); }
    if (this.exportsRequest) { this.exportsRequest.unsubscribe(); }
    if (this.downloadRequest) { this.downloadRequest.unsubscribe(); }
    if (this.csvRequest) { this.csvRequest.unsubscribe(); }
    if (this.quotaRequest) { this.quotaRequest.unsubscribe(); }
    if (this.toastTimer) { this.toastTimer.unsubscribe(); }
    if (this.csvFileHintTimer) { this.csvFileHintTimer.unsubscribe(); }
    if (this.companyNameDelay) { this.companyNameDelay.unsubscribe(); }
    if (this.saveSearchRequest) { this.saveSearchRequest.unsubscribe(); }
    if (this.searchesRequest) { this.searchesRequest.unsubscribe(); }
    if (this.searchCountsRequest) { this.searchCountsRequest.unsubscribe(); }
    if (this.allResultsCountRequest) { this.allResultsCountRequest.unsubscribe(); }
    if (this.restoreSearchRequest) { this.restoreSearchRequest.unsubscribe(); }
    this.pkdErrorDialog.nativeElement.close();
    if (this.pkdClassificationRequest) { this.pkdClassificationRequest.unsubscribe(); }
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    this.addressFields.forEach(field => { if (field.request) { field.request.unsubscribe(); } });
  }

  openCompany(row: CompanyShort, tab: 'description' | 'notes' = 'description') {
    this.detailsActiveTab = tab;
    this.selectedCompanyId = row.id;
    this.detailsCompany = row;
  }

  updateCompanyNotes(event: { companyId: number; notes: string }) {
    const company = this.companies.find(row => row.id === event.companyId);
    if (company) { company.userNotes = event.notes; }
    this.loadCompanies(false);
  }

  cyclePresence(key: 'hasPhone' | 'hasEmail' | 'hasWebsite', event: Event) {
    const value = this.filterDraft[key];
    this.filterDraft[key] = value === null ? true : value === true ? false : null;
    const checkbox = event.target as HTMLInputElement;
    checkbox.checked = this.filterDraft[key] === true;
    checkbox.indeterminate = this.filterDraft[key] === null;
    this.updateFilters();
  }

  presenceLabel(value: boolean | null) {
    return value === null ? 'Dowolne' : value ? 'Jest' : 'Brak';
  }

  setDateRangeEnabled(enabled: boolean) {
    this.dateRangeEnabled = enabled;
    if (!enabled) { this.updateFilters(); return; }
    if (!this.dateFrom) { this.dateFrom = '2020-01-01'; }
    if (!this.dateTo) { this.dateTo = this.todayDate(); }
    this.updateFilters();
  }

  private todayDate(): string {
    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: 'Europe/Warsaw', year: 'numeric', month: '2-digit', day: '2-digit'
    }).formatToParts(new Date());
    return ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)!.value).join('-');
  }

  get filtersChanged() {
    return this.addressFiltersChanged || this.lastUpdatedFilterState === null || this.filterState() !== this.lastUpdatedFilterState;
  }

  private filterState(recordLimit = this.filterDraft.recordLimit) {
    return JSON.stringify({
      pkdIds: this.selectedPkds.map(pkd => pkd.id).sort((a, b) => a - b),
      companyName: this.filterDraft.companyName || '',
      hasPhone: this.filterDraft.hasPhone,
      hasEmail: this.filterDraft.hasEmail,
      hasWebsite: this.filterDraft.hasWebsite,
      companyStatusIds: (this.filterDraft.companyStatusIds || [1]).slice().sort((a, b) => a - b),
      dateRangeEnabled: this.dateRangeEnabled,
      dateFrom: this.dateFrom,
      dateTo: this.dateTo,
      addressIds: this.addressFields.map(field => field.selectedId),
      radiusEnabled: this.radiusEnabled,
      radiusKm: this.radiusKm,
      recordLimit: Number(recordLimit)
    });
  }

  updateFilters() {
    if (!this.filtersChanged) { return; }
    if (!this.validateFilters()) { return; }
    const limit = this.maxRecordLimit;
    this.appliedFilters = {
      ...this.filterDraft, companyName: (this.filterDraft.companyName || '').trim().length >= 3 ? this.filterDraft.companyName.trim() : '', address: '', recordLimit: limit,
      wojewodztwoId: this.addressFields[0].selectedId || undefined,
      powiatId: this.addressFields[1].selectedId || undefined,
      gminaId: this.addressFields[2].selectedId || undefined,
      miejscowoscId: this.addressFields[3].selectedId || undefined,
      radiusKm: this.radiusAvailable && this.radiusEnabled ? this.radiusKm : undefined,
      addressLocality: undefined,
      startedFrom: this.dateRangeEnabled ? this.dateFrom : undefined,
      startedTo: this.dateRangeEnabled ? this.dateTo : undefined
    };
    this.lastUpdatedFilterState = this.filterState(limit);
    this.addressFiltersChanged = false;
    this.resultTabs.forEach(tab => tab.page = 1);
    this.page = 1;
    this.loadCompanies();
  }

  private validDate(value: string) {
    if (!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value) || value.substring(0, 4) === '0000') { return false; }
    const date = new Date(value + 'T00:00:00Z');
    return !isNaN(date.getTime()) && date.toISOString().substring(0, 10) === value;
  }

  private validateFilters() {
    this.filterError = '';
    if (this.dateRangeEnabled && (!this.validDate(this.dateFrom) || !this.validDate(this.dateTo) || this.dateTo < this.dateFrom)) {
      this.filterError = 'Podaj poprawne daty YYYY-MM-DD. Data do nie moze byc wczesniejsza niz data od.';
      return false;
    }
    return true;
  }

  setCompanyName(event: Event) {
    const value = this.sanitizeNameInput(event);
    this.filterDraft.companyName = value;
    if (this.companyNameDelay) { this.companyNameDelay.unsubscribe(); }
    if (value.length > 0 && !value.trim()) { return; }
    if (value.trim().length >= 3 || !value.trim()) {
      this.companyNameDelay = timer(3000).subscribe(() => this.updateFilters());
    }
  }

  setSearchName(event: Event) {
    const input = event.target as HTMLInputElement;
    this.searchName = this.sanitizeNameInput(event).slice(0, 128);
    input.value = this.searchName;
  }

  private sanitizeNameInput(event: Event): string {
    const input = event.target as HTMLInputElement;
    input.value = input.value.replace(/[^A-Za-z0-9\u0104\u0105\u0106\u0107\u0118\u0119\u0141\u0142\u0143\u0144\u00d3\u00f3\u015a\u015b\u0179\u017a\u017b\u017c _!().,'"-]/g, '');
    return input.value;
  }

  findPkd() {
    this.pkdHint = null;
    this.updatePkdSuggestions();
  }

  checkPkdDescription() {
    if (!this.pkdDescription.length || this.filtersLocked) { return; }
    this.clearPkds();
    this.classifyingPkd = true;
    this.pkdClassificationMessage = '';
    this.pkdClassificationError = '';
    this.pkdClassificationRequest = this.api.classifyPkd(this.pkdDescription)
      .pipe(finalize(() => this.classifyingPkd = false)).subscribe(result => {
        const firstLine = (result.text || result.status || '').split(/\r?\n/)[0].trim();
        if (firstLine !== 'OK') {
          this.pkdClassificationMessage = firstLine;
          this.pkdErrorDialog.nativeElement.showModal();
          return;
        }
        const additions = result.pkdCodes.filter(pkd => !this.selectedPkds.some(selected => selected.id === pkd.id));
        const available = this.maxSelectedPkds - this.selectedPkds.length;
        if (additions.length > available) {
          this.pkdClassificationError = 'Lista Wybrane PKD moze zawierac maksymalnie 10 kodow. Usun kody, aby dodac pozostale.';
        }
        if (available > 0 && additions.length) {
          this.selectedPkds = this.selectedPkds.concat(additions.slice(0, available));
          this.selectedPkdId = additions[0].id;
          this.updatePkdSuggestions();
          this.resultTabs.forEach(tab => tab.page = 1);
          this.page = 1;
          this.loadCompanies();
        }
      }, err => {
        this.pkdClassificationError = err.error && err.error.message ? err.error.message : 'Nie udalo sie dobrac kodow PKD.';
      });
  }

  acknowledgePkdError() {
    this.clearPkds();
    this.pkdErrorDialog.nativeElement.close();
  }

  showPkdHint(event: MouseEvent, pkd: Pkd) {
    const option = event.currentTarget as HTMLElement;
    if (option.scrollWidth <= option.clientWidth) { this.pkdHint = null; return; }
    const rect = option.getBoundingClientRect();
    const above = rect.bottom + 50 > window.innerHeight;
    const hintWidth = Math.min(option.scrollWidth + 14, window.innerWidth - 24);
    this.pkdHint = {
      text: this.pkdLabel(pkd),
      left: Math.max(12, Math.min(rect.left, window.innerWidth - hintWidth - 12)),
      top: above ? null : rect.bottom + 8,
      bottom: above ? window.innerHeight - rect.top + 8 : null
    };
  }

  navigatePkd(event: KeyboardEvent, selected: boolean) {
    const pkds = selected ? this.selectedPkds : this.pkdSuggestions;
    if (!pkds.length) { return; }
    const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End'];
    if (keys.indexOf(event.key) < 0) { return; }
    event.preventDefault();
    this.pkdHint = null;
    const currentId = selected ? this.selectedPkdId : this.foundPkdId;
    const current = pkds.findIndex(pkd => pkd.id === currentId);
    const index = event.key === 'Home' ? 0 : event.key === 'End' ? pkds.length - 1 :
      Math.max(0, Math.min(pkds.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1)));
    if (selected) { this.selectedPkdId = pkds[index].id; }
    else { this.foundPkdId = pkds[index].id; }
    const listbox = event.currentTarget as HTMLElement;
    (listbox.children[index] as HTMLElement).scrollIntoView({ block: 'nearest' });
  }

  hidePkdHint(event: MouseEvent) {
    const next = event.relatedTarget as HTMLElement | null;
    if (!next || !next.closest || !next.closest('.pkd-tooltip')) { this.pkdHint = null; }
  }

  addPkd(pkd: Pkd) {
    this.pkdHint = null;
    if (this.selectedPkds.length >= this.maxSelectedPkds) { return; }

    if (!this.selectedPkds.some(x => x.id === pkd.id)) {
      this.selectedPkds = this.sortPkds(this.selectedPkds.concat(pkd));
      this.resultTabs.forEach(tab => tab.page = 1);
      this.page = 1;
      this.loadCompanies();
    }
    this.pkdSuggestions = this.pkdSuggestions.filter(x => x.id !== pkd.id);
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
    this.selectedPkdId = pkd.id;
  }

  addSelectedFoundPkd() {
    if (this.selectedPkds.length >= this.maxSelectedPkds) { return; }

    const pkd = this.pkdSuggestions.find(x => x.id === Number(this.foundPkdId));
    if (pkd) { this.addPkd(pkd); }
  }

  removePkd(pkd: Pkd) {
    this.pkdHint = null;
    this.selectedPkds = this.selectedPkds.filter(x => x.id !== pkd.id);
    this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
    this.resultTabs.forEach(tab => tab.page = 1);
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  removeSelectedPkd() {
    const pkd = this.selectedPkds.find(x => x.id === Number(this.selectedPkdId));
    if (pkd) { this.removePkd(pkd); }
  }

  clearPkds() {
    this.pkdHint = null;
    if (!this.selectedPkds.length) { return; }
    this.selectedPkds = [];
    this.selectedPkdId = null;
    this.resultTabs.forEach(tab => tab.page = 1);
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  selectSearch(profile: SearchProfile) {
    if (this.publicPageTitle) { this.router.navigate(['/dashboard'], { queryParams: { searchId: profile.id } }); return; }
    if (this.savingSearch || this.classifyingPkd || this.exportingCsv) { return; }
    if (profile.id === this.currentSearchId) { this.selectDashboardTab('search'); return; }
    this.rememberDraft();
    if (this.companyNameDelay) { this.companyNameDelay.unsubscribe(); }
    this.searchSaved = profile.id > 0;
    this.currentSearchId = profile.id;
    this.hasNotes = false;
    this.activeDashboardTab = 'search';
    this.loadCsvExports();
    this.activeResultTab = this.resultTabs[0];
    if (this.restoreSearchRequest) { this.restoreSearchRequest.unsubscribe(); }
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    this.restoringSearch = true;
    this.addressFields.forEach(field => {
      if (field.request) { field.request.unsubscribe(); field.request = null; }
      field.options = []; field.visible = []; field.selectedId = null; field.term = ''; field.error = ''; field.loading = false;
    });
    this.radiusEnabled = false; this.radiusKm = 1;
    this.searchName = profile.name;
    this.selectedPkds = this.sortPkds(profile.pkdCodes.slice());
    this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
    const criteria = profile.criteria;
    this.sortBy = criteria && criteria.sortColumn ? criteria.sortColumn : 'dataAktywnosci';
    this.direction = criteria && (criteria.sortDirection || '').toLowerCase() === 'asc' ? 'asc' : 'desc';
    this.filterDraft = {
      companyName: criteria ? criteria.companyName || '' : '',
      address: '',
      hasPhone: criteria && criteria.hasPhone != null ? criteria.hasPhone : null,
      hasEmail: criteria && criteria.hasEmail != null ? criteria.hasEmail : null,
      hasWebsite: criteria && criteria.hasWebsite != null ? criteria.hasWebsite : null,
      companyStatusIds: criteria && criteria.companyStatusIds ? criteria.companyStatusIds.slice() : [1],
      recordLimit: this.maxRecordLimit
    };
    this.dateRangeEnabled = criteria ? criteria.dateRangeEnabled : false;
    this.dateFrom = criteria && criteria.dateRangeEnabled ? criteria.dateFrom || '2020-01-01' : '2020-01-01';
    this.dateTo = criteria && criteria.dateRangeEnabled ? criteria.dateTo || this.todayDate() : this.todayDate();
    this.appliedFilters = {
      ...this.filterDraft,
      startedFrom: this.dateRangeEnabled ? this.dateFrom : undefined,
      startedTo: this.dateRangeEnabled ? this.dateTo : undefined
    };
    this.filterError = '';
    this.pkdHint = null;
    this.updatePkdSuggestions();
    const ids = criteria ? [criteria.wojewodztwoId, criteria.powiatId, criteria.gminaId, criteria.miejscowoscId] : [];
    const finish = () => {
      this.radiusEnabled = !!criteria && criteria.hasFirmyWPromieniu === true && this.radiusAvailable;
      this.radiusKm = criteria && this.radiusOptions.includes(criteria.firmyWPromieniu) ? criteria.firmyWPromieniu : 1;
      ['wojewodztwoId', 'powiatId', 'gminaId', 'miejscowoscId'].forEach((key, index) => {
        this.appliedFilters[key] = this.addressFields[index].selectedId === null ? undefined : this.addressFields[index].selectedId;
      });
      this.appliedFilters.radiusKm = this.radiusEnabled ? this.radiusKm : undefined;
      this.lastUpdatedFilterState = this.filterState();
      this.savedSearchState = profile.id > 0 ? this.searchState() : null;
      this.addressFiltersChanged = false;
      this.resultTabs.forEach(tab => tab.page = 1);
      this.page = 1;
      this.restoringSearch = false;
      this.loadCompanies();
      this.refreshSearchCounts(this.searches);
    };
    this.restoreSearchRequest = this.restoreAddressSelection(ids, 0).subscribe(finish, () => {
      finish();
      this.filterError = 'Nie udalo sie odtworzyc wszystkich filtrow adresu.';
    });
  }

  private restoreAddressSelection(ids: (number | null | undefined)[], index: number): Observable<void> {
    const field = this.addressFields[index];
    field.loading = true;
    return this.api.addressOptions(field.level, this.addressFields.slice(0, index).map(parent => parent.selectedId))
      .pipe(finalize(() => field.loading = false), switchMap(options => {
        field.options = options.slice()
          .sort((a, b) => this.addressCollator.compare(a.name, b.name) || a.id - b.id);
        field.visible = field.options;
        field.selectedId = field.options.some(option => option.id === ids[index]) ? ids[index] : null;
        return field.selectedId !== null && index < 3 ? this.restoreAddressSelection(ids, index + 1) : of(undefined);
      }));
  }

  private currentCriteria(): SearchCriteria {
    return {
      sortColumn: this.sortBy, sortDirection: this.direction, companyName: this.filterDraft.companyName || '', address: '',
      hasPhone: this.filterDraft.hasPhone, hasEmail: this.filterDraft.hasEmail, hasWebsite: this.filterDraft.hasWebsite,
      companyStatusIds: (this.filterDraft.companyStatusIds || [1]).slice(),
      dateRangeEnabled: this.dateRangeEnabled, dateFrom: this.dateRangeEnabled ? this.dateFrom : null,
      dateTo: this.dateRangeEnabled ? this.dateTo : null, recordLimit: Number(this.filterDraft.recordLimit),
      wojewodztwoId: this.addressFields[0].selectedId, powiatId: this.addressFields[1].selectedId,
      gminaId: this.addressFields[2].selectedId, miejscowoscId: this.addressFields[3].selectedId,
      hasFirmyWPromieniu: this.radiusEnabled, firmyWPromieniu: this.radiusKm
    };
  }

  saveSearch(confirmDuplicateCriteria = false) {
    if (!this.canSaveSearch || !this.validateFilters()) { return; }
    const savedState = this.searchState();
    const draftId = this.currentSearchId;
    const persistedId = draftId !== null && draftId > 0 ? draftId : null;
    this.error = '';
    const criteria = this.currentCriteria();
    this.savingSearch = true;
    this.saveSearchRequest = this.api.saveSearch(this.searchName.trim(), this.selectedPkds.map(x => x.id), criteria, persistedId, confirmDuplicateCriteria)
      .pipe(finalize(() => this.savingSearch = false)).subscribe(profile => {
        const index = this.searches.findIndex(search => search.id === draftId || search.id === profile.id);
        if (index >= 0) { this.searches.splice(index, 1); }
        this.searches.unshift(profile);
      if (typeof profile.recordCount === 'number') { this.searchRecordCounts[profile.id] = profile.recordCount; }
        if (profile.recordCount == null && draftId !== null && this.searchRecordCounts[draftId] !== undefined) {
          this.searchRecordCounts[profile.id] = this.searchRecordCounts[draftId];
          if (draftId < 0) { delete this.searchRecordCounts[draftId]; }
        }
        this.currentSearchId = profile.id;
        this.loadCsvExports();
        this.savedSearchState = savedState;
        this.searchSaved = true;
        this.loadCompanies();
      }, err => {
        if (persistedId === null && err.status === 409 && err.error && err.error.code === 'DuplicateSearchCriteria') {
          this.duplicateSearchDialog.nativeElement.showModal();
          return;
        }
        this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie zapisac wyszukiwania.';
      });
  }

  closeDuplicateSearch() { this.duplicateSearchDialog.nativeElement.close(); }

  confirmDuplicateSearch() {
    this.closeDuplicateSearch();
    this.saveSearch(true);
  }

  get canSaveSearch() {
    return !this.searchPlanExpired && !!this.searchName.trim() && this.searchName.length <= 128 && !this.savingSearch && !this.restoringSearch && !this.classifyingPkd
      && ((this.currentSearchId !== null && this.currentSearchId > 0) || this.canPersistNewSearch)
      && (this.savedSearchState === null || this.searchState() !== this.savedSearchState);
  }

  private searchState() {
    return JSON.stringify({ name: this.searchName.trim(), sortBy: this.sortBy, direction: this.direction, filters: this.filterState() });
  }

  sort(column: string) {
    if (column === 'details' || this.loading) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.resultTabs.forEach(tab => tab.page = 1);
    this.page = 1;
    this.loadCompanies();
  }

  totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  formatNumber(value: number) { return this.numberFormatter.format(value); }
  emailUrl(email: string) { return this.api.emailUrl(email); }
  websiteHref(website: string) { return this.api.websiteHref(website); }

  formatPhone(value: string | null | undefined) {
    return this.api.formatPhone(value);
  }
  next() { if (!this.loading && this.page < this.totalPages()) { this.page++; this.loadCompanies(false); } }
  prev() { if (!this.loading && this.page > 1) { this.page--; this.loadCompanies(false); } }
  last() { if (!this.loading) { this.page = this.totalPages(); this.loadCompanies(false); } }

  exportingCsv = false;
  csvExportBlockedUntilUtc: string | null = null;
  get csvExportBlocked() { return !!this.csvExportBlockedUntilUtc && Date.parse(this.csvExportBlockedUntilUtc) > Date.now(); }
  csvExportsRemaining: number | null = null;
  csvQuotaLoading = false;
  csvQuotaLoaded = false;
  csvToast = '';
  csvFileHintSearchId: number | null = null;
  private csvFileHintTimer: Subscription | null = null;
  private quotaRequest: Subscription | null = null;
  private quotaSearchId: number | null = null;
  private quotaToastPending = false;
  private toastTimer: Subscription | null = null;

  private loadCsvExportQuota(showToast = false) {
    if (this.csvQuotaLoading && this.quotaSearchId === this.currentSearchId) {
      this.quotaToastPending = this.quotaToastPending || showToast;
      return;
    }
    if (this.quotaRequest) { this.quotaRequest.unsubscribe(); }
    this.quotaSearchId = this.currentSearchId;
    this.quotaToastPending = showToast;
    this.csvExportsRemaining = null;
    this.csvQuotaLoaded = false;
    this.csvQuotaLoading = false;
    if (this.currentSearchId === null || this.currentSearchId <= 0) { return; }
    this.csvQuotaLoading = true;
    this.quotaRequest = this.api.csvExportQuota(this.currentSearchId)
      .pipe(timeout(15000), take(1), finalize(() => this.csvQuotaLoading = false)).subscribe(quota => {
        this.csvQuotaLoading = false;
        this.csvExportsRemaining = quota.remaining;
        this.csvExportBlockedUntilUtc = quota.exportBlockedUntilUtc || null;
        this.csvQuotaLoaded = true;
        if (this.quotaToastPending) {
          this.quotaToastPending = false;
          this.csvToast = quota.remaining == null ? 'Eksport CSV zapisany. Brak limitu eksportow w planie.'
            : 'Eksport CSV zapisany. Pozostalo eksportow dla tego wyszukiwania: ' + this.formatNumber(quota.remaining) + '.';
    if (this.toastTimer) { this.toastTimer.unsubscribe(); }
          this.toastTimer = timer(6000).subscribe(() => this.csvToast = '');
        }
      }, () => {
        this.csvExportsRemaining = 0;
        this.error = 'Nie udalo sie pobrac limitu eksportow CSV.';
      });
  }
  private csvRequest: Subscription | null = null;
  private pendingCsvExport: (() => void) | null = null;

  confirmDuplicateExport() {
    this.duplicateExportDialog.nativeElement.close();
    const exportAction = this.pendingCsvExport;
    this.pendingCsvExport = null;
    if (exportAction) { exportAction(); }
  }

  cancelDuplicateExport() {
    this.duplicateExportDialog.nativeElement.close();
    this.pendingCsvExport = null;
    this.exportingCsv = false;
  }

  exportCsv() {
    if (this.searchPlanExpired) { return; }
    if (this.loading || this.total <= 0 || this.exportingCsv || this.csvQuotaLoading || this.csvExportsRemaining === 0 || this.csvExportBlocked) { return; }
    if (!this.validateFilters()) { return; }
    if (this.currentSearchId === null || this.currentSearchId <= 0) {
      this.error = 'Zapisz wyszukiwanie przed eksportem do CSV.';
      return;
    }
    this.error = '';
    this.exportingCsv = true;
    const searchId = this.currentSearchId;
    const searchName = this.searchName.trim();
    const days = this.activeResultTab.days;
    const resultTab = this.activeResultTab.key;
    const notesOnly = this.activeResultTab.key === 'notes';
    const sortBy = this.sortBy;
    const direction = this.direction;
    const criteria = this.currentCriteria();
    const savedState = this.searchState();
    const prepareSearch = this.canSaveSearch
      ? this.api.saveSearch(this.searchName.trim(), this.selectedPkds.map(x => x.id), criteria, searchId).pipe(map(profile => {
          const index = this.searches.findIndex(search => search.id === searchId);
          if (index >= 0) { this.searches.splice(index, 1); }
          this.searches.unshift(profile);
          this.savedSearchState = savedState;
          return profile;
        }))
      : of(null);
    this.csvRequest = prepareSearch.pipe(switchMap(() => this.api.checkCsvExport(searchId))).subscribe(result => {
      const exportAction = () => this.performCsvExport(searchId, days, sortBy, direction, searchName, notesOnly, resultTab);
      if (result.exists) {
        this.pendingCsvExport = exportAction;
        this.duplicateExportDialog.nativeElement.showModal();
      } else { exportAction(); }
    }, () => {
      this.exportingCsv = false;
      this.error = 'Nie udalo sie sprawdzic poprzednich eksportow CSV.';
    });
  }

  private performCsvExport(searchId: number, days: number | undefined, sortBy: string, direction: string, searchName: string, notesOnly = false, resultTab?: string) {
    this.csvRequest = this.api.exportCompanies(searchId, days, sortBy, direction, searchName, notesOnly, resultTab)
      .pipe(finalize(() => this.exportingCsv = false)).subscribe(response => {
      if (!response.body) { return; }
      const profile = this.searches.find(search => search.id === searchId);
      if (profile) { profile.hasCsvExports = true; }
      const disposition = response.headers.get('Content-Disposition') || '';
      const utf8Name = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
      const plainName = /filename="([^"]+)"/i.exec(disposition);
      this.saveCsvFile(response.body, utf8Name ? decodeURIComponent(utf8Name[1]) : plainName ? plainName[1] : 'firmy.CSV');
      if (this.csvFileHintTimer) { this.csvFileHintTimer.unsubscribe(); }
      this.csvFileHintSearchId = searchId;
      this.csvFileHintTimer = timer(5000).subscribe(() => this.csvFileHintSearchId = null);
      this.loadCsvExports(false);
      this.loadCsvExportQuota(true);
    }, err => {
      this.loadCsvExportQuota();
      this.error = err.status === 429 && err.error && err.error.message ? err.error.message
        : err.status === 409 ? 'Osiagnieto limit eksportow CSV dla tego wyszukiwania.'
        : 'Nie udalo sie zapisac lub pobrac eksportu CSV.';
    });
  }

  private sortPkds(pkds: Pkd[]) {
    return pkds.slice().sort((a, b) => this.pkdLabel(a).localeCompare(this.pkdLabel(b)));
  }

  private updatePkdSuggestions() {
    const term = this.pkdTerm.trim().toLowerCase();
    this.pkdSuggestions = this.allPkds.filter(pkd => {
      const isAlreadySelected = this.selectedPkds.some(selected => selected.id === pkd.id || selected.code === pkd.code);
      return !isAlreadySelected && (!term || this.pkdLabel(pkd).toLowerCase().indexOf(term) >= 0);
    });
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
  }

  pkdLabel(pkd: Pkd) {
    return `${pkd.code} ${pkd.name}`;
  }
}
