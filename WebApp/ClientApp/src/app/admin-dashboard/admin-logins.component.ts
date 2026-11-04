import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged, finalize } from 'rxjs/operators';
import { ApiService, AdminUserLogin } from '../services/api.service';

@Component({
  selector: 'app-admin-logins',
  templateUrl: './admin-logins.component.html',
  styleUrls: ['./admin-logins.component.css']
})
export class AdminLoginsComponent implements OnInit, OnDestroy {
  rows: AdminUserLogin[] = [];
  total = 0;
  page = 1;
  readonly pageSize = 10;
  sortBy = 'date';
  direction = 'desc';
  loading = false;
  error = '';
  emailFilter = '';
  private appliedEmailFilter = '';
  private filterChanges = new Subject<string>();
  private filterRequest: Subscription | null = null;
  private request: Subscription | null = null;
  deletingId: number | null = null;
  private deleteRequest: Subscription | null = null;
  constructor(private api: ApiService) {}
  get totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  ngOnInit() {
    this.filterRequest = this.filterChanges.pipe(debounceTime(300), distinctUntilChanged()).subscribe(email => {
      this.appliedEmailFilter = email;
      this.page = 1;
      this.load();
    });
    this.load();
  }
  filterByEmail(value: string) {
    this.emailFilter = value;
    this.filterChanges.next(value.trim());
  }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true; this.error = '';
    this.request = this.api.adminLogins(this.page, this.sortBy, this.direction, this.appliedEmailFilter).pipe(finalize(() => this.loading = false)).subscribe(result => {
      this.rows = result.items; this.total = result.total; this.page = result.page;
    }, () => { this.rows = []; this.error = 'Nie udalo sie pobrac historii logowan.'; });
  }
  sort(column: string) {
    if (this.loading || !['date', 'user'].includes(column)) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column; this.page = 1; this.load();
  }
  changePage(page: number) {
    if (this.loading || page < 1 || page > this.totalPages) { return; }
    this.page = page; this.load();
  }
  ngOnDestroy() {
    if (this.deleteRequest) { this.deleteRequest.unsubscribe(); }
    if (this.filterRequest) { this.filterRequest.unsubscribe(); }
    this.filterChanges.complete();
    if (this.request) { this.request.unsubscribe(); }
  }
  deleteLogin(row: AdminUserLogin) {
    if (this.loading || this.deletingId !== null) { return; }
    this.deletingId = row.userLoginId;
    this.error = '';
    this.deleteRequest = this.api.deleteAdminLogin(row.userLoginId)
      .pipe(finalize(() => this.deletingId = null)).subscribe(() => this.load(), () => {
        this.error = 'Nie udalo sie usunac wpisu logowania.';
      });
  }
}
