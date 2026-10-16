import { AfterViewInit, ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { retry, timeout } from 'rxjs/operators';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';

declare const google: any;

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html'
})
export class LoginComponent implements AfterViewInit, OnInit, OnDestroy {
  companyCount: number | null = null;
  companyCountError = false;
  private countRequest: Subscription | null = null;
  private countFormatter = new Intl.NumberFormat('pl-PL');

  ngOnInit() {
    this.loadCompanyCount();
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
    if (this.countRequest) { this.countRequest.unsubscribe(); }
  }

  formatCompanyCount() { return this.companyCount === null ? '' : this.countFormatter.format(this.companyCount); }
  error = '';
  isLoading = true;
  currentOrigin = window.location.origin;

  constructor(
    private api: ApiService,
    private router: Router,
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
            width: 320
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
    this.api.me().subscribe(me => {
      this.router.navigateByUrl(me.isAdmin ? '/admin' : (me.hasActivePlan ? '/dashboard' : '/plans'));
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
