import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { switchMap, timeout } from 'rxjs/operators';
import { Router } from '@angular/router';
import { ApiService } from './services/api.service';
import { BlockedAccountService } from './services/blocked-account.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html'
})
export class AppComponent implements AfterViewInit, OnDestroy {
  @ViewChild('expiredTrialDialog', { static: true }) expiredTrialDialog: ElementRef;
  @ViewChild('blockedAccountDialog', { static: true }) blockedAccountDialog: ElementRef;
  private blockedAccountSubscription: Subscription;
  private logoutSubscription: Subscription;
  loggingOutBlockedAccount = false;
  private trialNoticeSubscription: Subscription;
  private sessionExpiryTimer: number;

  constructor(private api: ApiService, private router: Router, private blockedAccount: BlockedAccountService) {}

  ngAfterViewInit() {
    this.sessionExpiryTimer = window.setInterval(() => {
      if (!this.blockedAccount.blocked && this.api.isAuthenticated() && !this.api.hasToken()) {
        this.api.logout();
        this.router.navigateByUrl('/');
      }
    }, 1000);
    this.blockedAccountSubscription = this.blockedAccount.notice.subscribe(show => {
      const dialog = this.blockedAccountDialog.nativeElement as HTMLDialogElement;
      if (show && !dialog.open) { dialog.showModal(); }
      else if (!show && dialog.open) { dialog.close(); }
    });
    this.trialNoticeSubscription = this.api.expiredTrialNotice.subscribe(show => {
      const dialog = this.expiredTrialDialog.nativeElement as HTMLDialogElement;
      if (show && !dialog.open) { dialog.showModal(); }
      else if (!show && dialog.open) { dialog.close(); }
    });
  }

  acknowledgeExpiredTrial() {
    this.api.acknowledgeExpiredTrial();
    this.router.navigateByUrl('/plans');
  }

  acknowledgeBlockedAccount() {
    if (this.loggingOutBlockedAccount) { return; }
    this.loggingOutBlockedAccount = true;
    const finish = () => {
      this.api.logout();
      this.blockedAccount.clear();
      this.loggingOutBlockedAccount = false;
      this.router.navigateByUrl('/');
    };
    this.logoutSubscription = this.api.refreshCsrf().pipe(
      switchMap(() => this.api.logoutSession()), timeout(10000)
    ).subscribe(() => finish(), () => finish());
  }

  ngOnDestroy() {
    window.clearInterval(this.sessionExpiryTimer);
    if (this.trialNoticeSubscription) { this.trialNoticeSubscription.unsubscribe(); }
    if (this.blockedAccountSubscription) { this.blockedAccountSubscription.unsubscribe(); }
    if (this.logoutSubscription) { this.logoutSubscription.unsubscribe(); }
  }
  title = 'app';
}
