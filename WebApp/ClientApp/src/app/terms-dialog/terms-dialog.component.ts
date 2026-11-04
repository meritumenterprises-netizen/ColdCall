import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize, timeout } from 'rxjs/operators';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { TermsConsentService } from '../services/terms-consent.service';

@Component({
  selector: 'app-terms-dialog',
  templateUrl: './terms-dialog.component.html',
  styleUrls: ['./terms-dialog.component.css']
})
export class TermsDialogComponent implements AfterViewInit, OnDestroy {
  @ViewChild('dialog', { static: true }) dialog: ElementRef;
  @ViewChild('content', { static: true }) content: ElementRef;
  html = '';
  isBetaUser = false;
  loading = false;
  saving = false;
  error = '';
  reachedBottom = false;
  private loaded = false;
  private visibility: Subscription | null = null;
  private request: Subscription | null = null;
  private decision: Subscription | null = null;
  private frame: number | null = null;
  private onContentLoad = () => this.checkScroll();
  constructor(private api: ApiService, private consent: TermsConsentService, private router: Router) {}
  ngAfterViewInit() {
    this.content.nativeElement.addEventListener('load', this.onContentLoad, true);
    this.visibility = this.consent.visible.subscribe(visible => {
      const dialog = this.dialog.nativeElement as HTMLDialogElement;
      if (visible) { if (!dialog.open) { dialog.showModal(); } this.load(); }
      else if (dialog.open) { dialog.close(); }
    });
  }
  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true; this.loaded = false; this.reachedBottom = false; this.error = ''; this.html = '';
    this.isBetaUser = false;
    this.request = this.api.termsOfService().pipe(timeout(15000), finalize(() => this.loading = false)).subscribe(row => {
      this.html = row.regulamin || '';
      this.isBetaUser = !!row.isBetaUser;
      this.loaded = true;
      this.content.nativeElement.scrollTop = 0;
      this.frame = window.requestAnimationFrame(() => { this.frame = null; this.checkScroll(); });
    }, () => this.error = 'Nie udalo sie pobrac regulaminu. Sprobuj ponownie.');
  }
  checkScroll() {
    const content = this.content.nativeElement as HTMLElement;
    this.reachedBottom = this.loaded && content.scrollTop + content.clientHeight >= content.scrollHeight - 2;
  }
  decide(accepted: boolean) {
    if (!this.reachedBottom || this.loading || this.saving) { return; }
    this.saving = true; this.error = '';
    this.decision = this.api.decideTermsOfService(accepted).pipe(finalize(() => this.saving = false)).subscribe(() => {
      if (accepted) { this.consent.accept(); }
      else {
        this.api.logout();
        this.consent.decline();
        window.alert('Odm\u00f3wiono warunk\u00f3w u\u017cycia serwisu.');
        this.router.navigateByUrl('/');
      }
    }, () => this.error = 'Nie udalo sie zapisac decyzji. Sprobuj ponownie.');
  }
  ngOnDestroy() {
    if (this.visibility) { this.visibility.unsubscribe(); }
    if (this.request) { this.request.unsubscribe(); }
    if (this.decision) { this.decision.unsubscribe(); }
    if (this.frame !== null) { window.cancelAnimationFrame(this.frame); }
    this.content.nativeElement.removeEventListener('load', this.onContentLoad, true);
  }
}
