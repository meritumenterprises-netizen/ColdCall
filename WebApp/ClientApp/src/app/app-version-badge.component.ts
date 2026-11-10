import { Component } from '@angular/core';
import { ApiService } from './services/api.service';

@Component({
  selector: 'app-version-badge',
  template: '<ng-container *ngIf="version | async as result"><span *ngIf="result.appVersion" class="app-version-badge">{{ result.appVersion }}</span></ng-container>',
  styles: [':host { display: inline-flex; min-width: 0; } .app-version-badge { padding: 3px 7px; border-radius: 4px; background: #dcfce7; color: #166534; font-size: 12px; font-weight: 600; line-height: 1.4; overflow-wrap: anywhere; }']
})
export class AppVersionBadgeComponent {
  readonly version = this.api.appVersionBadge();
  constructor(private api: ApiService) {}
}
