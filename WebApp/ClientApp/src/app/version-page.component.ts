import { Component, OnDestroy, OnInit } from '@angular/core';
import { Location } from '@angular/common';
import { Subscription } from 'rxjs';
import { ApiService } from './services/api.service';
import { ANGULAR_APP_VERSION } from './version';

@Component({
  selector: 'app-version-page',
  template: `<p class="frontend-version">Wersja frontendu: <strong>{{ frontendVersion }}</strong></p>
    <p>Wersja backendu: <strong>{{ backendVersion || (error ? '-' : 'Wczytywanie...') }}</strong></p>
    <p *ngIf="error" class="error" role="alert">{{ error }} <button type="button" class="ghost-button" (click)="load()">Pon\u00f3w</button></p>
    <button type="button" class="ghost-button" (click)="goBack()">Wr\u00f3\u0107</button>`,
  styles: ['.frontend-version { padding: 12px 0; }']
})
export class VersionPageComponent implements OnInit, OnDestroy {
  readonly frontendVersion = ANGULAR_APP_VERSION;
  backendVersion = '';
  error = '';
  private request: Subscription | null = null;
  constructor(private api: ApiService, private location: Location) {}
  goBack() { this.location.back(); }
  ngOnInit() { this.load(); }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.error = '';
    this.request = this.api.applicationVersion().subscribe(result => this.backendVersion = result.backendVersion,
      () => this.error = 'Nie uda\u0142o si\u0119 pobra\u0107 wersji backendu.');
  }
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
