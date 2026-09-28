import { Component, OnInit } from '@angular/core';
import { ApiService, AppUser, Company } from '../services/api.service';

@Component({
  selector: 'app-admin-dashboard',
  templateUrl: './admin-dashboard.component.html'
})
export class AdminDashboardComponent implements OnInit {
  tab: 'companies' | 'users' = 'companies';
  companies: Company[] = [];
  users: AppUser[] = [];
  page = 1;
  total = 0;
  filter = '';
  sortBy = 'name';
  direction = 'asc';
  message = '';
  planCode = 'PX5';

  companyColumns = ['name', 'businessType', 'firstName', 'lastName', 'wojewodztwo', 'city', 'gmina', 'powiat', 'postalCode', 'street', 'buildingNumber', 'apartmentNumber', 'phone', 'email', 'googleEmail', 'website', 'instagramAccount', 'facebookAccount'];

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadCompanies();
    this.loadUsers();
  }

  loadCompanies() {
    this.api.adminCompanies(this.page, this.sortBy, this.direction, this.filter).subscribe(x => {
      this.companies = x.items;
      this.total = x.total;
    });
  }

  loadUsers() { this.api.users().subscribe(x => this.users = x); }

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

  next() { if (this.page * 20 < this.total) { this.page++; this.loadCompanies(); } }
  prev() { if (this.page > 1) { this.page--; this.loadCompanies(); } }
}
