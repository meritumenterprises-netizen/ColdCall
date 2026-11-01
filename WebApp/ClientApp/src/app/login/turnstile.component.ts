import { AfterViewInit, Component, ElementRef, EventEmitter, NgZone, OnDestroy, Output, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { timeout } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

interface TurnstileApi {
  render(container: HTMLElement, options: any): string;
  reset(id: string): void;
  remove(id: string): void;
}

let scriptPromise: Promise<TurnstileApi> | null = null;
function loadTurnstile(): Promise<TurnstileApi> {
  const current = (window as any).turnstile as TurnstileApi;
  if (current) { return Promise.resolve(current); }
  if (!scriptPromise) {
    scriptPromise = new Promise<TurnstileApi>((resolve, reject) => {
      const script = document.createElement('script');
      let timer: any;
      const fail = () => {
        clearTimeout(timer); script.onload = null; script.onerror = null;
        script.remove(); scriptPromise = null; reject(new Error('Turnstile unavailable'));
      };
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
      script.async = true;
      script.onload = () => {
        clearTimeout(timer);
        const api = (window as any).turnstile as TurnstileApi;
        if (api) { resolve(api); } else { fail(); }
      };
      script.onerror = fail;
      timer = setTimeout(fail, 15000);
      document.head.appendChild(script);
    });
  }
  return scriptPromise;
}

@Component({
  selector: 'app-turnstile',
  template: `<div #widget></div><div *ngIf="error" role="alert">{{ error }}
    <button type="button" (click)="load()">Ponow weryfikacje</button></div>`,
  styles: [':host { display:block; grid-column:1 / -1; min-width:0; }']
})
export class TurnstileComponent implements AfterViewInit, OnDestroy {
  @ViewChild('widget', { static: true }) widget: ElementRef;
  @Output() tokenChange = new EventEmitter<string>();
  error = '';
  private destroyed = false;
  private request: Subscription | null = null;
  private api: TurnstileApi | null = null;
  private widgetId: string | null = null;
  constructor(private service: ApiService, private zone: NgZone) {}
  ngAfterViewInit() { this.load(); }
  load() {
    this.error = ''; this.reset();
    if (this.api && this.widgetId !== null) { this.api.remove(this.widgetId); this.widgetId = null; }
    if (this.request) { this.request.unsubscribe(); }
    this.request = this.service.registrationConfig().pipe(timeout(10000)).subscribe(config => {
      if (!config.configured || !config.siteKey) { this.failed(); return; }
      loadTurnstile().then(api => {
        if (this.destroyed) { return; }
        this.api = api;
        this.widgetId = api.render(this.widget.nativeElement, {
          sitekey: config.siteKey, action: 'register', theme: 'light', language: 'pl',
          size: this.widget.nativeElement.clientWidth < 300 ? 'compact' : 'flexible',
          callback: (token: string) => this.zone.run(() => { if (!this.destroyed) { this.error = ''; this.tokenChange.emit(token); } }),
          'expired-callback': () => this.zone.run(() => this.reset()),
          'timeout-callback': () => this.zone.run(() => this.reset()),
          'error-callback': () => { this.zone.run(() => this.failed()); return true; }
        });
      }).catch(() => this.zone.run(() => this.failed()));
    }, () => this.failed());
  }
  reset() {
    if (this.destroyed) { return; }
    this.tokenChange.emit('');
    if (this.api && this.widgetId !== null) { this.api.reset(this.widgetId); }
  }
  private failed() {
    if (this.destroyed) { return; }
    this.tokenChange.emit(''); this.error = 'Nie udalo sie uruchomic weryfikacji antybotowej.';
  }
  ngOnDestroy() {
    this.destroyed = true;
    if (this.request) { this.request.unsubscribe(); }
    if (this.api && this.widgetId !== null) { this.api.remove(this.widgetId); }
  }
}
