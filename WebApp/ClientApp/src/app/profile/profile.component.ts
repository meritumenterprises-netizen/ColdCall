import { ChangeDetectorRef, Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService, CurrentPlan, UserProfile } from '../services/api.service';

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.css']
})
export class ProfileComponent implements OnInit, OnDestroy {
  @ViewChild('purchaseDialog', { static: true }) purchaseDialog: ElementRef;
  purchasedPlan: CurrentPlan | null = null;
  plan: CurrentPlan | null = null;
  profile: UserProfile | null = null;
  loading = true;
  error = '';
  private request: Subscription | null = null;

  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router,
    private changeDetector: ChangeDetectorRef) {}

  acknowledgePurchase() {
    this.purchaseDialog.nativeElement.close();
    this.router.navigateByUrl('/dashboard');
  }

  get isDataPanel() { return this.route.snapshot.data.section === 'data'; }

  get canSearch(): boolean {
    const user = this.api.user();
    return !!this.profile && !this.profile.isAdmin && !!user && user.hasActivePlan;
  }

  ngOnInit() { this.loadPlan(); }

  loadPlan() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.request = this.api.profile().pipe(finalize(() => this.loading = false)).subscribe(user => {
      this.profile = user;
      this.plan = user.plan || null;
      if (!this.isDataPanel && !user.isAdmin) {
        const purchase = this.api.takePurchasedPlan();
        this.purchasedPlan = purchase && this.plan && purchase.code === this.plan.code ? this.plan : purchase;
        if (this.purchasedPlan) {
          this.changeDetector.detectChanges();
          this.purchaseDialog.nativeElement.showModal();
        }
      }
    }, () => this.error = 'Nie udało się pobrać danych profilu.');
  }

  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
