import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-public-page',
  template: `<app-landing-menu *ngIf="!api.isAuthenticated()"></app-landing-menu>
    <app-admin-dashboard *ngIf="api.isAuthenticated() && api.isAdmin()" [publicPageTitle]="title"></app-admin-dashboard>
    <app-user-dashboard *ngIf="api.isAuthenticated() && !api.isAdmin()" [publicPageTitle]="title"></app-user-dashboard>
    <main *ngIf="!api.isAuthenticated()" class="public-page">
    <h1>{{ title }}</h1>
    <app-guides *ngIf="isGuidePage"></app-guides>
    <app-feedback *ngIf="title === 'Feedback'"></app-feedback>
    <app-feedback *ngIf="title === 'RODO'" [rodo]="true"></app-feedback>
    <app-feedback *ngIf="title === 'Wsparcie techniczne'" [support]="true"></app-feedback>
    <app-public-regulamin *ngIf="title === 'Regulamin'"></app-public-regulamin>
    <app-accounting-offices *ngIf="title === 'Biura rachunkowe'"></app-accounting-offices>
    <app-marketing-agencies *ngIf="title === 'Agencje marketingowe'"></app-marketing-agencies>
    <app-lead-generation *ngIf="title === 'Agencje lead generation'"></app-lead-generation>
    <app-version-page *ngIf="title === 'Wersja aplikacji FindMyClient'"></app-version-page>
    </main>`,
  styles: [`.public-page { max-width: 1100px; margin: 0 auto; padding: 32px 24px; }
    h1 { font-size: 28px; margin: 28px 0; } a { text-decoration: none; font-weight: 700; }`]
})
export class PublicPageComponent {
  constructor(private route: ActivatedRoute, public api: ApiService) {}
  get title(): string { return this.route.snapshot.data.title; }
  get isGuidePage(): boolean { return this.route.snapshot.routeConfig.path.indexOf('poradniki') === 0; }
}
