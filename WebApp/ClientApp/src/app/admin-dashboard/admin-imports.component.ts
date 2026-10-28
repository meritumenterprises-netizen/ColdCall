import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService, AdminImport } from '../services/api.service';

@Component({
  selector: 'app-admin-imports',
  templateUrl: './admin-imports.component.html',
  styleUrls: ['./admin-imports.component.css']
})
export class AdminImportsComponent implements OnInit, OnDestroy {
  imports: AdminImport[] = [];
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
