import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';

import { AppComponent } from './app.component';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { LoginComponent } from './login/login.component';
import { UserDashboardComponent } from './user-dashboard/user-dashboard.component';
import { AddressListboxComponent } from './user-dashboard/address-listbox.component';
import { AdminDashboardComponent } from './admin-dashboard/admin-dashboard.component';
import { PlansComponent } from './plans/plans.component';
import { ProfileComponent } from './profile/profile.component';
import { AuthGuard } from './auth.guard';
import { CompanyDetailsComponent } from './company-details/company-details.component';

@NgModule({
  declarations: [
    AppComponent,
    NavMenuComponent,
    LoginComponent,
    UserDashboardComponent,
    AddressListboxComponent,
    AdminDashboardComponent,
    PlansComponent,
    ProfileComponent,
    CompanyDetailsComponent
  ],
  imports: [
    BrowserModule.withServerTransition({ appId: 'ng-cli-universal' }),
    HttpClientModule,
    FormsModule,
    RouterModule.forRoot([
      { path: '', component: LoginComponent, pathMatch: 'full' },
      { path: 'dashboard', component: UserDashboardComponent, canActivate: [AuthGuard] },
      { path: 'admin', component: AdminDashboardComponent, canActivate: [AuthGuard] },
      { path: 'plans', component: PlansComponent, canActivate: [AuthGuard] },
      { path: 'profile', redirectTo: 'profile/plan', pathMatch: 'full' },
      { path: 'profile/plan', component: ProfileComponent, canActivate: [AuthGuard] },
    ])
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }
