import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize, timeout } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-public-regulamin',
  template: `<p *ngIf="loading" role="status">Wczytywanie...</p>
    <div *ngIf="error" class="error" role="alert">{{ error }} <button type="button" (click)="load()">Pon&#243;w</button></div>
    <div *ngIf="!loading && !error" class="regulamin-content" [innerHTML]="html"></div>`,
  styles: [`.regulamin-content { line-height: 1.6; overflow-wrap: anywhere; overflow-x: auto; }
    .regulamin-content ::ng-deep img { max-width: 100%; height: auto; }`]
})
export class PublicRegulaminComponent implements OnInit, OnDestroy {
  loading = false;
  error = '';
  html = '';
  private request: Subscription | null = null;
  constructor(private api: ApiService) {}
  ngOnInit() { this.load(); }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true; this.error = ''; this.html = '';
    this.request = this.api.publicRegulamin().pipe(timeout(15000), finalize(() => this.loading = false)).subscribe(row => {
      this.html = row.regulamin || '';
    }, () => this.error = 'Nie udalo sie pobrac regulaminu.');
  }
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
