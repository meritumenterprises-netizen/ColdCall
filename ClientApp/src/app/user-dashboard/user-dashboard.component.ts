import { Component, OnInit } from '@angular/core';
import { ApiService, Company, Pkd, SearchProfile } from '../services/api.service';

@Component({
  selector: 'app-user-dashboard',
  templateUrl: './user-dashboard.component.html'
})
export class UserDashboardComponent implements OnInit {
  searches: SearchProfile[] = [];
  selectedPkds: Pkd[] = [];
  pkdTerm = '';
  pkdSuggestions: Pkd[] = [];
  companies: Company[] = [];
  total = 0;
  page = 1;
  sortBy = 'name';
  direction = 'asc';
  searchName = '';
  error = '';

  columns = [
    ['name', 'Nazwa firmy'], ['firstName', 'Imie'], ['lastName', 'Nazwisko'], ['krs', 'KRS'], ['nip', 'NIP'],
    ['city', 'Miejscowosc'], ['postalCode', 'Kod'], ['street', 'Ulica'], ['buildingNumber', 'Budynek'],
    ['apartmentNumber', 'Lokal'], ['phone', 'Telefon'], ['email', 'Email'], ['website', 'WWW'], ['startedAt', 'Start']
  ];

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadSearches();
    this.loadCompanies();
  }

  loadSearches() { this.api.searches().subscribe(x => this.searches = x); }

  loadCompanies() {
    this.error = '';
    this.api.companies(this.page, this.sortBy, this.direction, this.selectedPkds.map(x => x.id)).subscribe(result => {
      this.companies = result.items;
      this.total = result.total;
    }, err => this.error = err.status === 402 ? 'Plan wygasl. Przejdz do zakupow, aby odblokowac wyniki.' : 'Nie udalo sie pobrac firm.');
  }

  findPkd() {
    if (this.pkdTerm.length < 2) { this.pkdSuggestions = []; return; }
    this.api.pkd(this.pkdTerm).subscribe(x => this.pkdSuggestions = x);
  }

  addPkd(pkd: Pkd) {
    if (!this.selectedPkds.some(x => x.id === pkd.id)) {
      this.selectedPkds.push(pkd);
      this.page = 1;
      this.loadCompanies();
    }
    this.pkdTerm = '';
    this.pkdSuggestions = [];
  }

  removePkd(pkd: Pkd) {
    this.selectedPkds = this.selectedPkds.filter(x => x.id !== pkd.id);
    this.loadCompanies();
  }

  selectSearch(profile: SearchProfile) {
    this.searchName = profile.name;
    this.selectedPkds = profile.pkdCodes.slice();
    this.page = 1;
    this.loadCompanies();
  }

  saveSearch() {
    if (!this.searchName.trim() || this.selectedPkds.length === 0) { return; }
    this.api.saveSearch(this.searchName.trim(), this.selectedPkds.map(x => x.id)).subscribe(profile => {
      this.searches.push(profile);
    }, err => this.error = err.error && err.error.message ? err.error.message : 'Nie udalo sie zapisac wyszukiwania.');
  }

  sort(column: string) {
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.loadCompanies();
  }

  next() { if (this.page * 20 < this.total) { this.page++; this.loadCompanies(); } }
  prev() { if (this.page > 1) { this.page--; this.loadCompanies(); } }

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
}
