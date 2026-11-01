import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClientModule, HTTP_INTERCEPTORS } from '@angular/common/http';
import { BlockedAccountInterceptor } from './services/blocked-account.interceptor';
import { RouterModule } from '@angular/router';

import { AppComponent } from './app.component';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { LoginComponent } from './login/login.component';
import { MaintenanceComponent } from './maintenance/maintenance.component';
import { MaintenanceGuard } from './maintenance/maintenance.guard';
import { UserDashboardComponent } from './user-dashboard/user-dashboard.component';
import { AddressListboxComponent } from './user-dashboard/address-listbox.component';
import { AdminDashboardComponent } from './admin-dashboard/admin-dashboard.component';
import { AdminPlansComponent } from './admin-dashboard/admin-plans.component';
import { AdminImportsComponent } from './admin-dashboard/admin-imports.component';
import { AdminGlobalSettingsComponent } from './admin-dashboard/admin-global-settings.component';
import { PlansComponent } from './plans/plans.component';
import { PaymentSuccessComponent } from './plans/payment-success.component';
import { ProfileComponent } from './profile/profile.component';
import { AuthGuard } from './auth.guard';
import { LandingGuard } from './landing.guard';
import { CompanyDetailsComponent } from './company-details/company-details.component';
import { CompanyNotesIndicatorComponent } from './company-notes-indicator/company-notes-indicator.component';

@NgModule({
  declarations: [
    AppComponent,
    NavMenuComponent,
    LoginComponent,
    MaintenanceComponent,
    UserDashboardComponent,
    AddressListboxComponent,
    AdminDashboardComponent,
    AdminPlansComponent,
    AdminImportsComponent,
    AdminGlobalSettingsComponent,
    PlansComponent,
    PaymentSuccessComponent,
    ProfileComponent,
    CompanyDetailsComponent,
    CompanyNotesIndicatorComponent
  ],
  imports: [
    BrowserModule.withServerTransition({ appId: 'ng-cli-universal' }),
    HttpClientModule,
    FormsModule,
    RouterModule.forRoot([
      { path: '', component: LoginComponent, pathMatch: 'full', canActivate: [LandingGuard] },
      { path: 'maintenance', component: MaintenanceComponent, canActivate: [MaintenanceGuard] },
      { path: 'dashboard', component: UserDashboardComponent, canActivate: [AuthGuard] },
      { path: 'admin', component: AdminDashboardComponent, canActivate: [AuthGuard] },
      { path: 'plans', component: PlansComponent, canActivate: [AuthGuard] },
      { path: 'payment/success', component: PaymentSuccessComponent, canActivate: [AuthGuard] },
      { path: 'profile', redirectTo: 'profile/plan', pathMatch: 'full' },
      { path: 'profile/plan', component: ProfileComponent, canActivate: [AuthGuard] },
      { path: 'profile/data', component: ProfileComponent, canActivate: [AuthGuard], data: { section: 'data' } },
      { path: 'profile/purchases', component: ProfileComponent, canActivate: [AuthGuard], data: { section: 'purchases' } },
    ])
  ],
  providers: [{ provide: HTTP_INTERCEPTORS, useClass: BlockedAccountInterceptor, multi: true }],
  bootstrap: [AppComponent]
})
export class AppModule { }
