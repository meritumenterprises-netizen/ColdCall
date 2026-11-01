import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
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
  private request: Subscription | null = null;
  constructor(private api: ApiService) {}
  get totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  ngOnInit() { this.load(); }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true; this.error = '';
    this.request = this.api.adminLogins(this.page, this.sortBy, this.direction).pipe(finalize(() => this.loading = false)).subscribe(result => {
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
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
