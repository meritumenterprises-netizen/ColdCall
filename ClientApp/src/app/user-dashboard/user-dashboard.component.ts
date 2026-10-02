import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ApiService, CompanyFilters, CompanyShort, Pkd, SearchProfile, SortableColumn } from '../services/api.service';

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
  activeFilterTab: 'pkd' | 'criteria' = 'pkd';
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

  columns: SortableColumn[] = [
    { key: 'name', label: 'Nazwa' },
    { key: 'nip', label: 'NIP' },
    { key: 'fullAddress', label: 'Adres' },
    { key: 'phone', label: 'Telefon' },
    { key: 'email', label: 'Email' },
    { key: 'www', label: 'Witryna WWW' },
    { key: 'dataAktywnosci', label: 'Data aktywno\u015bci' },
    { key: 'details', label: 'Szczeg\u00f3\u0142y' }
  ];

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.lastUpdatedFilterState = this.filterState();
    this.loadSearches();
    this.loadPkds();
    this.loadCompanies();
  }

  loadSearches() { this.api.searches().subscribe(x => this.searches = x); }

  selectFilterTab(tab: 'pkd' | 'criteria') {
    this.activeFilterTab = tab;
    this.pkdHint = null;
  }

  navigateFilterTabs(event: KeyboardEvent) {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].indexOf(event.key) === -1) { return; }
    event.preventDefault();
    const tab = event.key === 'Home' ? 'pkd' : event.key === 'End' ? 'criteria' :
      this.activeFilterTab === 'pkd' ? 'criteria' : 'pkd';
    this.selectFilterTab(tab);
    const buttons = (event.currentTarget as HTMLElement).parentElement.querySelectorAll<HTMLButtonElement>('[role="tab"]');
    buttons[tab === 'pkd' ? 0 : 1].focus();
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
        this.columns = result.sortableColumns.filter(column => column.key !== 'id')
          .map(column => column.key === 'typyDzialalnosci' ? { key: 'details', label: 'Szczeg\u00f3\u0142y' } : column);
      }
    }, err => {
      this.companies = [];
      this.total = 0;
      this.error = err.status === 402 ? 'Plan wygasl. Przejdz do zakupow, aby odblokowac wyniki.' : 'Nie udalo sie pobrac firm.';
    });
  }

  ngOnDestroy() {
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
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
  }

  presenceLabel(value: boolean | null) {
    return value === null ? 'Dowolne' : value ? 'Jest' : 'Brak';
  }

  setDateRangeEnabled(enabled: boolean) {
    this.dateRangeEnabled = enabled;
    if (!enabled) { return; }
    if (!this.dateFrom) { this.dateFrom = '2020-01-01'; }
    if (!this.dateTo) {
      const parts = new Intl.DateTimeFormat('en-GB', {
        timeZone: 'Europe/Warsaw', year: 'numeric', month: '2-digit', day: '2-digit'
      }).formatToParts(new Date());
      this.dateTo = ['year', 'month', 'day'].map(type => parts.find(part => part.type === type)!.value).join('-');
    }
  }

  get filtersChanged() {
    return this.lastUpdatedFilterState === null || this.filterState() !== this.lastUpdatedFilterState;
  }

  private filterState() {
    return JSON.stringify({
      pkdIds: this.selectedPkds.map(pkd => pkd.id).sort((a, b) => a - b),
      companyName: this.filterDraft.companyName || '',
      hasPhone: this.filterDraft.hasPhone,
      hasEmail: this.filterDraft.hasEmail,
      hasWebsite: this.filterDraft.hasWebsite,
      dateRangeEnabled: this.dateRangeEnabled,
      dateFrom: this.dateFrom,
      dateTo: this.dateTo,
      recordLimit: Number(this.filterDraft.recordLimit)
    });
  }

  updateFilters() {
    if (this.loading || !this.filtersChanged) { return; }
    if (!this.validateFilters()) { return; }
    const limit = Number(this.filterDraft.recordLimit);
    this.appliedFilters = {
      ...this.filterDraft, companyName: (this.filterDraft.companyName || '').trim(), address: '', recordLimit: limit,
      startedFrom: this.dateRangeEnabled ? this.dateFrom : undefined,
      startedTo: this.dateRangeEnabled ? this.dateTo : undefined
    };
    this.lastUpdatedFilterState = this.filterState();
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
    this.filterDraft.recordLimit = value;
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
    this.searchName = profile.name;
    this.selectedPkds = this.sortPkds(profile.pkdCodes.slice());
    this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
    const criteria = profile.criteria;
    this.sortBy = criteria ? criteria.sortColumn : 'dataAktywnosci';
    this.direction = criteria ? criteria.sortDirection : 'desc';
    this.filterDraft = {
      companyName: criteria ? criteria.companyName || '' : '',
      address: '',
      hasPhone: criteria ? criteria.hasPhone : null,
      hasEmail: criteria ? criteria.hasEmail : null,
      hasWebsite: criteria ? criteria.hasWebsite : null,
      recordLimit: criteria ? criteria.recordLimit : 1000
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
    this.lastUpdatedFilterState = this.filterState();
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  saveSearch() {
    if (!this.searchName.trim() || !this.validateFilters()) { return; }
    const criteria = {
      sortColumn: this.sortBy, sortDirection: this.direction, companyName: this.filterDraft.companyName || '', address: '',
      hasPhone: this.filterDraft.hasPhone, hasEmail: this.filterDraft.hasEmail, hasWebsite: this.filterDraft.hasWebsite,
      dateRangeEnabled: this.dateRangeEnabled, dateFrom: this.dateRangeEnabled ? this.dateFrom : null,
      dateTo: this.dateRangeEnabled ? this.dateTo : null, recordLimit: Number(this.filterDraft.recordLimit)
    };
    this.api.saveSearch(this.searchName.trim(), this.selectedPkds.map(x => x.id), criteria).subscribe(profile => {
      this.searches.push(profile);
    }, err => this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie zapisac wyszukiwania.');
  }

  sort(column: string) {
    if (column === 'details' || !this.selectedPkds.length || this.loading) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.page = 1;
    this.loadCompanies();
  }

  totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  formatNumber(value: number) { return this.numberFormatter.format(value); }
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
      const isAlreadySelected = this.selectedPkds.some(selected => selected.id === pkd.id);
      return !isAlreadySelected && (!term || this.pkdLabel(pkd).toLowerCase().indexOf(term) >= 0);
    });
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
  }

  pkdLabel(pkd: Pkd) {
    return `${pkd.code} ${pkd.name}`;
  }
}
