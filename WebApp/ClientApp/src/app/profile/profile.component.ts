import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ApiService, CurrentPlan } from '../services/api.service';

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.css']
})
export class ProfileComponent implements OnInit, OnDestroy {
  plan: CurrentPlan | null = null;
  loading = true;
  error = '';
  private request: Subscription | null = null;

  constructor(private api: ApiService) {}

  ngOnInit() { this.loadPlan(); }

  loadPlan() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.request = this.api.me().pipe(finalize(() => this.loading = false)).subscribe(user => {
      this.plan = user.plan || null;
    }, () => this.error = 'Nie udało się pobrać szczegółów planu.');
  }

  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
