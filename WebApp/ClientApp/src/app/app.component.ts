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

  constructor(private api: ApiService, private router: Router) {}

  ngAfterViewInit() {
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

  ngOnDestroy() { if (this.trialNoticeSubscription) { this.trialNoticeSubscription.unsubscribe(); } }
  title = 'app';
}
