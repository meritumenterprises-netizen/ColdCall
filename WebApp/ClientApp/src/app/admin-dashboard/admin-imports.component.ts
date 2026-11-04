import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService, AdminImport } from '../services/api.service';
import { importRecordsPerSecond, importDurationSeconds } from './import-record-rate';

@Component({
  selector: 'app-admin-imports',
  templateUrl: './admin-imports.component.html',
  styleUrls: ['./admin-imports.component.css']
})
export class AdminImportsComponent implements OnInit, OnDestroy {
  recordsPerSecond = importRecordsPerSecond;
  imports: AdminImport[] = [];
  page = 1;
  readonly pageSize = 10;
  sortBy = 'startedAt';
  sortDirection = 'desc';
  columns = [
    { key: 'importDate', label: 'Data importu' },
    { key: 'importTargetRecords', label: 'Import Records' },
    { key: 'startedAt', label: 'Rozpocz\u0119to' },
    { key: 'finishedAt', label: 'Zako\u0144czono' },
    { key: 'duration', label: 'Czas trwania' },
    { key: 'rate', label: 'Rekord\u00f3w/sec' }
  ];
  sort(column: string) {
    if (this.loading || (column !== 'importId' && !this.columns.some(item => item.key === column))) { return; }
    this.sortDirection = this.sortBy === column && this.sortDirection === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.page = 1;
  }
  private sortValue(row: AdminImport): number | null {
    if (this.sortBy === 'importId') { return row.importId; }
    if (this.sortBy === 'rate') { return importRecordsPerSecond(row); }
    if (this.sortBy === 'duration') { return importDurationSeconds(row.duration); }
    if (this.sortBy === 'importTargetRecords') { return row.importTargetRecords; }
    const value = row[this.sortBy];
    if (!value) { return null; }
    const timestamp = new Date(value).getTime();
    return Number.isFinite(timestamp) ? timestamp : null;
  }
  get sortedImports() {
    return this.imports.slice().sort((a, b) => {
      const first = this.sortValue(a), second = this.sortValue(b);
      if (first == null || second == null) {
        return first == null && second == null ? b.importId - a.importId : first == null ? 1 : -1;
      }
      return (first - second) * (this.sortDirection === 'asc' ? 1 : -1) || b.importId - a.importId;
    });
  }
  get totalPages() { return Math.max(1, Math.ceil(this.imports.length / this.pageSize)); }
  get pagedImports() {
    const start = (this.page - 1) * this.pageSize;
    return this.sortedImports.slice(start, start + this.pageSize);
  }
  changePage(page: number) { this.page = Math.max(1, Math.min(page, this.totalPages)); }
  selectedId: number | null = null;
  summary = '';
  loading = true;
  summaryLoading = false;
  error = '';
  deletingIds = new Set<number>();
  private deleteRequests = new Set<Subscription>();
  private listRequest: Subscription;
  private summaryRequest: Subscription;

  constructor(private api: ApiService) {}
  ngOnInit() { this.load(); }
  load() {
    if (this.listRequest) { this.listRequest.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.listRequest = this.api.adminImports().subscribe(rows => {
      this.imports = rows;
      this.changePage(this.page);
      this.loading = false;
    }, () => {
      this.loading = false;
      this.error = 'Nie udalo sie pobrac importow.';
    });
  }
  select(row: AdminImport) {
    if (this.summaryRequest) { this.summaryRequest.unsubscribe(); }
    this.selectedId = row.importId;
    this.summary = '';
    this.error = '';
    this.summaryLoading = true;
    this.summaryRequest = this.api.adminImportSummary(row.importId).subscribe(result => {
      this.summary = result.summary || '';
      this.summaryLoading = false;
    }, () => {
      this.summaryLoading = false;
      this.error = 'Nie udalo sie pobrac podsumowania.';
    });
  }
  ngOnDestroy() {
    this.deleteRequests.forEach(request => request.unsubscribe());
    if (this.listRequest) { this.listRequest.unsubscribe(); }
    if (this.summaryRequest) { this.summaryRequest.unsubscribe(); }
  }
  deleteImport(row: AdminImport, event: Event) {
    event.stopPropagation();
    if (this.deletingIds.has(row.importId)) { return; }
    this.deletingIds.add(row.importId);
    this.error = '';
    const request = this.api.deleteAdminImport(row.importId).subscribe(() => {
      this.deletingIds.delete(row.importId);
      this.imports = this.imports.filter(item => item.importId !== row.importId);
      this.changePage(this.page);
      if (this.selectedId === row.importId) {
        if (this.summaryRequest) { this.summaryRequest.unsubscribe(); }
        this.selectedId = null;
        this.summary = '';
        this.summaryLoading = false;
      }
    }, () => {
      this.deletingIds.delete(row.importId);
      this.error = 'Nie udalo sie usunac importu.';
    });
    this.deleteRequests.add(request);
  }
}
