import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';

import { AppComponent } from './app.component';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { LoginComponent } from './login/login.component';
import { UserDashboardComponent } from './user-dashboard/user-dashboard.component';
import { AdminDashboardComponent } from './admin-dashboard/admin-dashboard.component';
import { PlansComponent } from './plans/plans.component';

@NgModule({
  declarations: [
    AppComponent,
    NavMenuComponent,
    LoginComponent,
    UserDashboardComponent,
    AdminDashboardComponent,
    PlansComponent
  ],
  imports: [
    BrowserModule.withServerTransition({ appId: 'ng-cli-universal' }),
    HttpClientModule,
    FormsModule,
    RouterModule.forRoot([
      { path: '', component: LoginComponent, pathMatch: 'full' },
      { path: 'dashboard', component: UserDashboardComponent },
      { path: 'admin', component: AdminDashboardComponent },
      { path: 'plans', component: PlansComponent },
    ])
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }
