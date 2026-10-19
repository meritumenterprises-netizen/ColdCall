import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { Router } from '@angular/router';
import { ApiService } from './services/api.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html'
})
export class AppComponent implements AfterViewInit, OnDestroy {
  @ViewChild('expiredTrialDialog', { static: true }) expiredTrialDialog: ElementRef;
  private trialNoticeSubscription: Subscription;
  private sessionExpiryTimer: number;

  constructor(private api: ApiService, private router: Router) {}

  ngAfterViewInit() {
    this.sessionExpiryTimer = window.setInterval(() => {
      if (this.api.isAuthenticated() && !this.api.hasToken()) {
        this.api.logout();
        this.router.navigateByUrl('/');
      }
    }, 1000);
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

  ngOnDestroy() {
    window.clearInterval(this.sessionExpiryTimer);
    if (this.trialNoticeSubscription) { this.trialNoticeSubscription.unsubscribe(); }
  }
  title = 'app';
}
