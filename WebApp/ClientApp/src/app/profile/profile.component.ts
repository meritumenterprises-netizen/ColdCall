import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ActivatedRoute } from '@angular/router';
import { ApiService, CurrentPlan, UserProfile } from '../services/api.service';

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.css']
})
export class ProfileComponent implements OnInit, OnDestroy {
  plan: CurrentPlan | null = null;
  profile: UserProfile | null = null;
  loading = true;
  error = '';
  private request: Subscription | null = null;

  constructor(private api: ApiService, private route: ActivatedRoute) {}

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
    }, () => this.error = 'Nie udało się pobrać danych profilu.');
  }

  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
