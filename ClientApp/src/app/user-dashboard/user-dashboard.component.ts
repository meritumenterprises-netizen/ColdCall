import { Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { Observable, of, Subscription } from 'rxjs';
import { finalize, switchMap } from 'rxjs/operators';
import { AddressLevel, AddressOption, ApiService, CompanyFilters, CompanyShort, Pkd, SearchProfile, SortableColumn } from '../services/api.service';

@Component({
  selector: 'app-user-dashboard',
  templateUrl: './user-dashboard.component.html',
  styleUrls: ['./user-dashboard.component.css']
})
export class UserDashboardComponent implements OnInit, OnDestroy {
  searches: SearchProfile[] = [];
  allPkds: Pkd[] = [];
  selectedPkds: Pkd[] = [];
  pkdTerm = '';
  pkdDescription = '';
  classifyingPkd = false;
  @ViewChild('pkdErrorDialog', { static: true }) pkdErrorDialog: ElementRef;
  pkdClassificationMessage = '';
  pkdClassificationError = '';
  private pkdClassificationRequest: Subscription | null = null;
  restoringSearch = false;
  private searchesRequest: Subscription | null = null;
  private restoreSearchRequest: Subscription | null = null;
  activeFilterTab: 'pkd' | 'criteria' | 'address' = 'pkd';
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
  private recordLimitChanged = false;
  readonly recordLimitOptions = [100, 500, 1000, 2000, 5000];
  readonly radiusOptions = [1, 5, 10, 20, 50, 100, 200, 500];
  pkdSuggestions: Pkd[] = [];
  maxSelectedPkds = 10;
  foundPkdId: number | null = null;
  selectedPkdId: number | null = null;
  pkdHint: { text: string; left: number; top: number | null; bottom: number | null } | null = null;
  companies: CompanyShort[] = [];
  selectedCompanyId: number | null = null;
  detailsCompany: CompanyShort | null = null;
  total = 0;
  readonly pageSize = 10;
  readonly loadingRows = Array.from({ length: 10 }, (_, index) => index);
  loading = false;
  private companiesRequest: Subscription | null = null;
  page = 1;
  sortBy = 'dataAktywnosci';
  direction = 'desc';
  searchName = '';
  currentSearchId: number | null = null;
  savingSearch = false;
  searchSaved = false;

  get filtersLocked() { return this.classifyingPkd || this.restoringSearch || this.searchSaved; }
  private savedSearchState: string | null = null;
  private saveSearchRequest: Subscription | null = null;
  error = '';
  filterError = '';
  recordLimitError = '';
  filterDraft: CompanyFilters = { companyName: '', address: '', hasPhone: null, hasEmail: null, hasWebsite: null, recordLimit: 1000 };
  appliedFilters: CompanyFilters = { ...this.filterDraft };
  private lastUpdatedFilterState: string | null = null;
  dateRangeEnabled = false;
  dateFrom = '';
  dateTo = '';
  private readonly numberFormatter = new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 0 });
  private readonly addressCollator = new Intl.Collator('pl', { sensitivity: 'base' });

  columns: SortableColumn[] = [
    { key: 'name', label: 'Nazwa' },
    { key: 'fullAddress', label: 'Adres' },
    { key: 'phone', label: 'Telefon' },
    { key: 'email', label: 'Email' },
    { key: 'www', label: 'Witryna WWW' },
    { key: 'dataAktywnosci', label: 'Aktywne od' },
    { key: 'details', label: 'Szczeg\u00f3\u0142y' }
  ];

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.lastUpdatedFilterState = this.filterState();
    this.loadSearches();
    this.loadPkds();
  }

  loadSearches() {
    this.restoringSearch = true;
    this.searchesRequest = this.api.searches().subscribe(profiles => {
      this.searches = profiles;
      if (profiles.length) { this.selectSearch(profiles[0]); }
      else { this.restoringSearch = false; this.loadAddressOptions(0); this.loadCompanies(); }
    }, () => {
      this.restoringSearch = false;
      this.loadAddressOptions(0);
      this.loadCompanies();
      this.filterError = 'Nie udalo sie pobrac zapisanych wyszukiwan.';
    });
  }

  selectFilterTab(tab: 'pkd' | 'criteria' | 'address') {
    this.activeFilterTab = tab;
    this.pkdHint = null;
  }

  navigateFilterTabs(event: KeyboardEvent) {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].indexOf(event.key) === -1) { return; }
    event.preventDefault();
    const tabs: ('pkd' | 'criteria' | 'address')[] = ['pkd', 'criteria', 'address'];
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
    return this.addressFields[0].selectedId !== null && this.addressFields[1].selectedId !== null;
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
        field.options = options.filter(option => index !== 0 || option.id !== 1)
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
    if (refresh) { this.updateFilters(false); }
  }

  setRadiusEnabled(enabled: boolean) {
    enabled = enabled && this.radiusAvailable;
    if (this.radiusEnabled === enabled) { return; }
    this.radiusEnabled = enabled;
    this.addressFiltersChanged = true;
    this.updateFilters(false);
  }

  setRadiusKm(km: number) {
    if (this.radiusKm === km) { return; }
    this.radiusKm = km;
    this.addressFiltersChanged = true;
    this.updateFilters(false);
  }

  loadPkds() {
    this.api.pkd().subscribe(x => {
      this.allPkds = this.sortPkds(x);
      this.updatePkdSuggestions();
    });
  }

  loadCompanies() {
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    this.error = '';
    this.loading = true;
    this.companiesRequest = this.api.companies(this.page, this.sortBy, this.direction, this.selectedPkds.map(x => x.id), this.appliedFilters)
      .pipe(finalize(() => this.loading = false))
      .subscribe(result => {
      this.companies = result.items;
      if (!this.companies.some(row => row.id === this.selectedCompanyId)) { this.selectedCompanyId = null; }
      this.total = result.total;
      this.page = result.page;
      if (result.sortableColumns && result.sortableColumns.length) {
        this.columns = result.sortableColumns.filter(column => column.key !== 'id' && column.key !== 'nip')
          .map(column => column.key === 'typyDzialalnosci' ? { key: 'details', label: 'Szczeg\u00f3\u0142y' } :
            column.key === 'dataAktywnosci' ? { ...column, label: 'Aktywne od' } : column);
      }
    }, err => {
      this.companies = [];
      this.total = 0;
      this.error = err.status === 402 ? 'Plan wygasl. Przejdz do zakupow, aby odblokowac wyniki.' :
        err.status === 400 && err.error && err.error.message ? err.error.message : 'Nie udalo sie pobrac firm.';
    });
  }

  ngOnDestroy() {
    if (this.saveSearchRequest) { this.saveSearchRequest.unsubscribe(); }
    if (this.searchesRequest) { this.searchesRequest.unsubscribe(); }
    if (this.restoreSearchRequest) { this.restoreSearchRequest.unsubscribe(); }
    this.pkdErrorDialog.nativeElement.close();
    if (this.pkdClassificationRequest) { this.pkdClassificationRequest.unsubscribe(); }
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    this.addressFields.forEach(field => { if (field.request) { field.request.unsubscribe(); } });
  }

  openCompany(row: CompanyShort) {
    this.selectedCompanyId = row.id;
    this.detailsCompany = row;
  }

  cyclePresence(key: 'hasPhone' | 'hasEmail' | 'hasWebsite', event: Event) {
    const value = this.filterDraft[key];
    this.filterDraft[key] = value === null ? true : value === true ? false : null;
    const checkbox = event.target as HTMLInputElement;
    checkbox.checked = this.filterDraft[key] === true;
    checkbox.indeterminate = this.filterDraft[key] === null;
    this.updateFilters(false);
  }

  presenceLabel(value: boolean | null) {
    return value === null ? 'Dowolne' : value ? 'Jest' : 'Brak';
  }

  setDateRangeEnabled(enabled: boolean) {
    this.dateRangeEnabled = enabled;
    if (!enabled) { this.updateFilters(false); return; }
    if (!this.dateFrom) { this.dateFrom = '2020-01-01'; }
    if (!this.dateTo) {
      const parts = new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Europe/Warsaw', year: 'numeric', month: '2-digit', day: '2-digit'
      }).formatToParts(new Date());
      this.dateTo = ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)!.value).join('-');
    }
    this.updateFilters(false);
  }

  get filtersChanged() {
    return this.recordLimitChanged || this.addressFiltersChanged || this.lastUpdatedFilterState === null || this.filterState() !== this.lastUpdatedFilterState;
  }

  private filterState(recordLimit = this.filterDraft.recordLimit) {
    return JSON.stringify({
      pkdIds: this.selectedPkds.map(pkd => pkd.id).sort((a, b) => a - b),
      companyName: this.filterDraft.companyName || '',
      hasPhone: this.filterDraft.hasPhone,
      hasEmail: this.filterDraft.hasEmail,
      hasWebsite: this.filterDraft.hasWebsite,
      dateRangeEnabled: this.dateRangeEnabled,
      dateFrom: this.dateFrom,
      dateTo: this.dateTo,
      addressIds: this.addressFields.map(field => field.selectedId),
      radiusEnabled: this.radiusEnabled,
      radiusKm: this.radiusKm,
      recordLimit: Number(recordLimit)
    });
  }

  updateFilters(applyRecordLimit = true) {
    if ((applyRecordLimit && this.loading) || !this.filtersChanged) { return; }
    if (!this.validateFilters()) { return; }
    const limit = Number(applyRecordLimit ? this.filterDraft.recordLimit : this.appliedFilters.recordLimit);
    this.appliedFilters = {
      ...this.filterDraft, companyName: (this.filterDraft.companyName || '').trim(), address: '', recordLimit: limit,
      wojewodztwoId: this.addressFields[0].selectedId || undefined,
      powiatId: this.addressFields[1].selectedId || undefined,
      gminaId: this.addressFields[2].selectedId || undefined,
      miejscowoscId: this.addressFields[3].selectedId || undefined,
      radiusKm: this.radiusAvailable && this.radiusEnabled && this.addressFields[3].selectedId !== null ? this.radiusKm : undefined,
      addressLocality: undefined,
      startedFrom: this.dateRangeEnabled ? this.dateFrom : undefined,
      startedTo: this.dateRangeEnabled ? this.dateTo : undefined
    };
    this.lastUpdatedFilterState = this.filterState(limit);
    this.addressFiltersChanged = false;
    if (applyRecordLimit) { this.recordLimitChanged = false; }
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
    if (!this.validateRecordLimit()) { return false; }
    if (this.dateRangeEnabled && (!this.validDate(this.dateFrom) || !this.validDate(this.dateTo) || this.dateTo <= this.dateFrom)) {
      this.filterError = 'Podaj poprawne daty YYYY-MM-DD. Data do musi by\u0107 p\u00f3\u017aniejsza ni\u017c data od.';
      return false;
    }
    return true;
  }

  setRecordLimit(value: number) {
    if (this.filterDraft.recordLimit === value) { return; }
    this.filterDraft.recordLimit = value;
    this.recordLimitChanged = true;
    this.validateRecordLimit();
  }

  private validateRecordLimit() {
    const limit = Number(this.filterDraft.recordLimit);
    this.recordLimitError = limit > 10000
      ? 'Nie mo\u017cna jednorazowo wy\u015bwietli\u0107 wi\u0119cej ni\u017c 10 000 rekord\u00f3w. Nale\u017cy zaw\u0119zi\u0107 kryteria poszukiwania.'
      : !Number.isInteger(limit) || limit < 1
        ? 'Liczba rekord\u00f3w musi by\u0107 dodatni\u0105 liczb\u0105 ca\u0142kowit\u0105.' : '';
    return !this.recordLimitError;
  }

  findPkd() {
    this.pkdHint = null;
    this.updatePkdSuggestions();
  }

  checkPkdDescription() {
    if (!this.pkdDescription.length || this.classifyingPkd) { return; }
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
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  selectSearch(profile: SearchProfile) {
    if (this.savingSearch || this.classifyingPkd) { return; }
    this.searchSaved = false;
    this.currentSearchId = profile.id;
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
      recordLimit: criteria && this.recordLimitOptions.includes(criteria.recordLimit) ? criteria.recordLimit : 1000
    };
    this.dateRangeEnabled = criteria ? criteria.dateRangeEnabled : false;
    this.dateFrom = criteria && criteria.dateRangeEnabled ? criteria.dateFrom || '' : '';
    this.dateTo = criteria && criteria.dateRangeEnabled ? criteria.dateTo || '' : '';
    this.appliedFilters = {
      ...this.filterDraft,
      startedFrom: this.dateRangeEnabled ? this.dateFrom : undefined,
      startedTo: this.dateRangeEnabled ? this.dateTo : undefined
    };
    this.filterError = '';
    this.recordLimitError = '';
    this.pkdHint = null;
    this.updatePkdSuggestions();
    const ids = criteria ? [criteria.wojewodztwoId, criteria.powiatId, criteria.gminaId, criteria.miejscowoscId] : [];
    const finish = () => {
      this.radiusEnabled = !!criteria && criteria.hasFirmyWPromieniu === true && this.radiusAvailable;
      this.radiusKm = criteria && this.radiusOptions.includes(criteria.firmyWPromieniu) ? criteria.firmyWPromieniu : 1;
      ['wojewodztwoId', 'powiatId', 'gminaId', 'miejscowoscId'].forEach((key, index) => {
        this.appliedFilters[key] = this.addressFields[index].selectedId === null ? undefined : this.addressFields[index].selectedId;
      });
      this.appliedFilters.radiusKm = this.radiusEnabled && this.addressFields[3].selectedId !== null ? this.radiusKm : undefined;
      this.lastUpdatedFilterState = this.filterState();
      this.savedSearchState = this.searchState();
      this.addressFiltersChanged = false;
      this.recordLimitChanged = false;
      this.page = 1;
      this.restoringSearch = false;
      this.loadCompanies();
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
        field.options = options.filter(option => index !== 0 || option.id !== 1)
          .sort((a, b) => this.addressCollator.compare(a.name, b.name) || a.id - b.id);
        field.visible = field.options;
        field.selectedId = field.options.some(option => option.id === ids[index]) ? ids[index] : null;
        return field.selectedId !== null && index < 3 ? this.restoreAddressSelection(ids, index + 1) : of(undefined);
      }));
  }

  saveSearch() {
    if (!this.canSaveSearch || !this.validateFilters()) { return; }
    const savedState = this.searchState();
    this.error = '';
    const criteria = {
      sortColumn: this.sortBy, sortDirection: this.direction, companyName: this.filterDraft.companyName || '', address: '',
      hasPhone: this.filterDraft.hasPhone, hasEmail: this.filterDraft.hasEmail, hasWebsite: this.filterDraft.hasWebsite,
      dateRangeEnabled: this.dateRangeEnabled, dateFrom: this.dateRangeEnabled ? this.dateFrom : null,
      dateTo: this.dateRangeEnabled ? this.dateTo : null, recordLimit: Number(this.filterDraft.recordLimit),
      wojewodztwoId: this.addressFields[0].selectedId, powiatId: this.addressFields[1].selectedId,
      gminaId: this.addressFields[2].selectedId, miejscowoscId: this.addressFields[3].selectedId,
      hasFirmyWPromieniu: this.radiusEnabled, firmyWPromieniu: this.radiusKm
    };
    this.savingSearch = true;
    this.saveSearchRequest = this.api.saveSearch(this.searchName.trim(), this.selectedPkds.map(x => x.id), criteria, this.currentSearchId)
      .pipe(finalize(() => this.savingSearch = false)).subscribe(profile => {
        const index = this.searches.findIndex(search => search.id === profile.id);
        if (index < 0) { this.searches.unshift(profile); }
        else { this.searches[index] = profile; }
        this.currentSearchId = profile.id;
        this.savedSearchState = savedState;
        this.searchSaved = true;
      }, err => this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie zapisac wyszukiwania.');
  }

  get canSaveSearch() {
    return !!this.searchName.trim() && !this.savingSearch && !this.restoringSearch && !this.classifyingPkd
      && (this.savedSearchState === null || this.searchState() !== this.savedSearchState);
  }

  private searchState() {
    return JSON.stringify({ name: this.searchName.trim(), sortBy: this.sortBy, direction: this.direction, filters: this.filterState() });
  }

  sort(column: string) {
    if (column === 'details' || this.loading) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.page = 1;
    this.loadCompanies();
  }

  totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  formatNumber(value: number) { return this.numberFormatter.format(value); }
  emailUrl(email: string) { return this.api.emailUrl(email); }
  websiteHref(website: string) { return this.api.websiteHref(website); }

  formatPhone(value: string | null | undefined) {
    if (!value) { return ''; }
    const phone = value.trim();
    if (/^48\d{9}$/.test(phone)) { return phone.replace(/^48(\d{3})(\d{3})(\d{3})$/, '+48 $1 $2 $3'); }
    if (/^0048\d{9}$/.test(phone)) { return phone.replace(/^0048(\d{3})(\d{3})(\d{3})$/, '+48 $1 $2 $3'); }
    if (/^\+48\d{9}$/.test(phone)) { return phone.replace(/^(\+48)(\d{3})(\d{3})(\d{3})$/, '$1 $2 $3 $4'); }
    return /^\d{9}$/.test(phone) ? phone.replace(/(\d{3})(\d{3})(\d{3})/, '$1 $2 $3') : value;
  }
  next() { if (this.page < this.totalPages()) { this.page++; this.loadCompanies(); } }
  prev() { if (this.page > 1) { this.page--; this.loadCompanies(); } }
  last() { this.page = this.totalPages(); this.loadCompanies(); }

  exportCsv() {
    this.api.exportCompanies(this.selectedPkds.map(x => x.id)).subscribe(blob => {
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'firmy.csv';
      link.click();
      window.URL.revokeObjectURL(url);
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
