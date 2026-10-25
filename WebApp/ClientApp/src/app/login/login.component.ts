import { AfterViewInit, ChangeDetectorRef, Component, ElementRef, NgZone, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize, retry, switchMap, tap, timeout } from 'rxjs/operators';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, Me, TrialOffer } from '../services/api.service';

declare const google: any;

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html'
})
export class LoginComponent implements AfterViewInit, OnInit, OnDestroy {
  @ViewChild('googleRequiredDialog', { static: true }) googleRequiredDialog: ElementRef;
  localMode: 'login' | 'register' | 'forgot' | 'reset' = 'login';
  localEmail = '';
  localDisplayName = '';
  localPassword = '';
  localPasswordConfirmation = '';
  localBusy = false;
  localError = '';
  localMessage = '';
  private resetToken = '';
  private localRequest: Subscription | null = null;

  setLocalMode(mode: 'login' | 'register' | 'forgot' | 'reset') {
    if (this.localBusy) { return; }
    this.localMode = mode; this.localError = ''; this.localMessage = '';
    this.localPassword = ''; this.localPasswordConfirmation = '';
  }

  private handleAuthenticated(me: Me, offer?: TrialOffer) {
    if (me.isAdmin) { this.router.navigateByUrl('/admin'); return; }
    if (!me.hasActivePlan && (me.hasExpiredPlan || me.hasExpiredTrial)) {
      return;
    }
    const trial = offer || me.trialOffer;
    if (trial) {
      this.trialOffer = trial;
      this.trialTermsAccepted = false;
      this.changeDetector.detectChanges();
      this.trialDialog.nativeElement.showModal();
      return;
    }
    this.router.navigateByUrl(me.isAdmin ? '/admin' : (me.hasActivePlan ? '/dashboard' : '/plans'));
  }

  submitLocalAuth() {
    if (this.localBusy) { return; }
    this.localError = ''; this.localMessage = '';
    const email = this.localEmail.trim();
    if (this.localMode === 'register' && /@(?:gmail|googlemail)\.com$/i.test(email)) { this.googleRequiredDialog.nativeElement.showModal(); return; }
    if ((this.localMode === 'register' || this.localMode === 'reset') && this.localPassword !== this.localPasswordConfirmation) {
      this.localError = 'Hasla musza byc takie same.'; return;
    }
    if (this.localMode === 'login') {
      this.localBusy = true;
      this.localRequest = this.api.loginLocal(email, this.localPassword).pipe(
        switchMap(result => this.api.me().pipe(tap(me => this.handleAuthenticated(me, result.trialOffer)))),
        finalize(() => this.localBusy = false)
      ).subscribe(() => this.localPassword = '', err => this.handleLocalError(err));
      return;
    }
    const request = this.localMode === 'register' ? this.api.registerLocal(this.localDisplayName, email, this.localPassword)
      : this.localMode === 'forgot' ? this.api.forgotPassword(email) : this.api.resetPassword(email, this.resetToken, this.localPassword);
    const wasReset = this.localMode === 'reset';
    this.localBusy = true;
    this.localRequest = request.pipe(finalize(() => this.localBusy = false)).subscribe(result => {
      this.localMessage = result.message;
      this.localPassword = ''; this.localPasswordConfirmation = '';
      if (wasReset) { this.localMode = 'login'; this.resetToken = ''; }
    }, err => this.handleLocalError(err));
  }

  resendConfirmation() {
    if (this.localBusy) { return; }
    this.localBusy = true; this.localError = '';
    this.localRequest = this.api.resendConfirmation(this.localEmail.trim()).pipe(finalize(() => this.localBusy = false))
      .subscribe(result => this.localMessage = result.message, err => this.handleLocalError(err));
  }

  private handleLocalError(err: any) {
    if (err.error && err.error.code === 'GoogleSignInRequired') { this.googleRequiredDialog.nativeElement.showModal(); return; }
    this.localError = err.status === 429 ? 'Zbyt wiele prob. Sprobuj ponownie za minute.'
      : err.error && err.error.message ? err.error.message : 'Nie udalo sie wykonac operacji. Sprobuj ponownie.';
  }
  @ViewChild('trialDialog', { static: true }) trialDialog: ElementRef;
  trialOffer: TrialOffer | null = null;
  trialTermsAccepted = false;
  trialSaving = false;
  trialError = '';
  private trialRequest: Subscription | null = null;

  acceptTrial() {
    if (!this.trialOffer || !this.trialTermsAccepted || this.trialSaving) { return; }
    this.trialSaving = true;
    this.trialError = '';
    this.trialRequest = this.api.acceptTrial(this.trialOffer.token, true).pipe(
      switchMap(() => this.api.me()), finalize(() => this.trialSaving = false)
    ).subscribe(() => {
      this.trialDialog.nativeElement.close();
      this.router.navigateByUrl('/dashboard');
    }, err => this.trialError = err.error && err.error.message ? err.error.message : 'Nie udalo sie aktywowac planu probnego.');
  }

  declineTrial() {
    if (this.trialSaving) { return; }
    this.trialDialog.nativeElement.close();
    this.router.navigateByUrl('/plans');
  }
  companyCount: number | null = null;
  companyCountError = false;
  private countRequest: Subscription | null = null;
  private countFormatter = new Intl.NumberFormat('pl-PL');

  ngOnInit() {
    this.loadCompanyCount();
    const query = this.route.snapshot.queryParams;
    if (query.email && query.token && (query.mode === 'confirm-email' || query.mode === 'reset-password')) {
      this.localEmail = query.email;
      this.resetToken = query.token;
      this.router.navigateByUrl('/', { replaceUrl: true });
      if (query.mode === 'reset-password') { this.localMode = 'reset'; }
      else {
        this.localBusy = true;
        this.localRequest = this.api.confirmEmail(this.localEmail, this.resetToken).pipe(finalize(() => this.localBusy = false))
          .subscribe(result => { this.localMessage = result.message; this.resetToken = ''; }, err => this.handleLocalError(err));
      }
    }
  }

  loadCompanyCount() {
    if (this.countRequest) { this.countRequest.unsubscribe(); }
    this.companyCountError = false;
    this.countRequest = this.api.companyCount().pipe(timeout(15000), retry(1)).subscribe(result => {
      this.zone.run(() => {
        this.companyCount = result.count;
        this.companyCountError = false;
      });
    }, () => this.zone.run(() => this.companyCountError = true));
  }

  ngOnDestroy() {
    if (this.localRequest) { this.localRequest.unsubscribe(); }
    if (this.trialRequest) { this.trialRequest.unsubscribe(); }
    if (this.countRequest) { this.countRequest.unsubscribe(); }
  }

  formatCompanyCount() { return this.companyCount === null ? '' : this.countFormatter.format(this.companyCount); }
  error = '';
  isLoading = true;
  currentOrigin = window.location.origin;

  constructor(
    private api: ApiService,
    private router: Router,
    private route: ActivatedRoute,
    private zone: NgZone,
    private changeDetector: ChangeDetectorRef) {}

  ngAfterViewInit() {
    var configLoaded = false;
    var configTimeout = window.setTimeout(() => {
      if (!configLoaded) {
        this.zone.run(() => this.setStatus(false, 'Nie udalo sie pobrac konfiguracji Google.'));
      }
    }, 8000);

    this.api.authConfig().subscribe(config => {
      configLoaded = true;
      window.clearTimeout(configTimeout);
      this.initializeGoogle(config.googleClientId);
    }, () => {
      configLoaded = true;
      window.clearTimeout(configTimeout);
      this.setStatus(false, 'Nie udalo sie pobrac konfiguracji Google.');
    });
  }

  private initializeGoogle(clientId: string) {
    if (!clientId) {
      this.setStatus(false, 'Brakuje Google Client ID w appsettings.json.');
      return;
    }

    this.loadGoogleScript(8000).then(() => {
      this.zone.run(() => {
        google.accounts.id.initialize({
          client_id: clientId,
          callback: (response: any) => this.zone.run(() => this.loginWithCredential(response.credential)),
          auto_select: false,
          cancel_on_tap_outside: false
        });

        google.accounts.id.renderButton(
          document.getElementById('googleSignInButton'),
          {
            theme: 'filled_blue',
            size: 'large',
            text: 'signin_with',
            shape: 'rectangular',
            logo_alignment: 'left',
            width: Math.min(320, document.getElementById('googleSignInButton').parentElement.clientWidth || Math.max(200, window.innerWidth - 104))
          }
        );
        this.setStatus(false);
      });
    }).catch((message: string) => {
      this.zone.run(() => this.setStatus(false, message || 'Nie udalo sie zaladowac Google Sign-In.'));
    });
  }

  private loginWithCredential(credential: string) {
    this.error = '';
    if (!credential) {
      this.error = 'Google nie zwrocil tokena logowania.';
      return;
    }
    this.api.setToken(credential);
    this.api.startGoogleSession().pipe(switchMap(() => this.api.me())).subscribe(me => {
      this.handleAuthenticated(me);
    }, error => {
      this.api.logout();
      this.error = this.describeLoginError(error);
    });
  }

  private describeLoginError(error: any): string {
    var details = '';
    if (error && error.error) {
      if (typeof error.error === 'string') {
        details = error.error;
      } else {
        details = error.error.errorDescription || error.error.failure || error.error.message || '';
      }
    }

    if (!details && error && error.message) {
      details = error.message;
    }

    return details
      ? 'Nie udalo sie potwierdzic tokena Google: ' + details
      : 'Nie udalo sie potwierdzic tokena Google. Status HTTP: ' + (error && error.status ? error.status : 'brak');
  }

  private loadGoogleScript(timeoutMs: number): Promise<void> {
    if ((window as any).google && (window as any).google.accounts) {
      return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
      var elapsed = 0;
      var interval = window.setInterval(() => {
        if ((window as any).google && (window as any).google.accounts) {
          window.clearInterval(interval);
          resolve();
          return;
        }

        elapsed += 100;
        if (elapsed >= timeoutMs) {
          window.clearInterval(interval);
          reject('Nie udalo sie zaladowac Google Sign-In. Sprawdz, czy przegladarka nie blokuje https://accounts.google.com/gsi/client.');
        }
      }, 100);
    });
  }

  private setStatus(isLoading: boolean, error: string = '') {
    this.isLoading = isLoading;
    this.error = error;
    this.changeDetector.detectChanges();
  }
}
