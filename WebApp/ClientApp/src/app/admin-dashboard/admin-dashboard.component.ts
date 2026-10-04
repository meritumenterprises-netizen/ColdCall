import { Component, OnInit } from '@angular/core';
import { ApiService, AppUser, BusinessType, Company, Pkd, SortableColumn } from '../services/api.service';

@Component({
  selector: 'app-admin-dashboard',
  templateUrl: './admin-dashboard.component.html'
})
export class AdminDashboardComponent implements OnInit {
  tab: 'companies' | 'users' = 'companies';
  companies: Company[] = [];
  users: AppUser[] = [];
  businessTypes: BusinessType[] = [];
  allPkds: Pkd[] = [];
  pageSize = 10;
  page = 1;
  total = 0;
  filter = '';
  selectedPkds: Pkd[] = [];
  pkdTerm = '';
  pkdSuggestions: Pkd[] = [];
  maxSelectedPkds = 10;
  foundPkdId: number | null = null;
  selectedPkdId: number | null = null;
  sortBy = 'name';
  direction = 'asc';
  message = '';
  planCode = 'PX5';

  companyColumns: SortableColumn[] = [];

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadPkds();
    this.loadBusinessTypes();
    this.loadCompanies();
    this.loadUsers();
  }

  loadCompanies() {
    this.api.adminCompanies(this.page, this.sortBy, this.direction, this.filter, this.selectedPkds.map(x => x.id)).subscribe(x => {
      this.companies = x.items;
      this.total = x.total;
      this.pageSize = x.pageSize;
      this.companyColumns = x.sortableColumns || [];
    });
  }

  loadUsers() { this.api.users().subscribe(x => this.users = x); }

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
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.loadCompanies();
  }

  save(company: Company) {
    this.api.updateCompany(company).subscribe(() => this.message = 'Zapisano zmiany w bazie.', () => this.message = 'Nie udalo sie zapisac zmian.');
  }

  grant(user: AppUser) {
    this.api.grantPlan(user.id, this.planCode).subscribe(() => {
      this.message = 'Plan zostal przyznany i wyslano email albo wpisano log, jesli SMTP nie jest skonfigurowane.';
      this.loadUsers();
    });
  }

  totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  next() { if (this.page < this.totalPages()) { this.page++; this.loadCompanies(); } }
  prev() { if (this.page > 1) { this.page--; this.loadCompanies(); } }
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
