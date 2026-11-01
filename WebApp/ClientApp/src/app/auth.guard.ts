import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router, RouterStateSnapshot } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService, Me } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}

  canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean | Observable<boolean> {
    if (this.api.isAuthenticated()) {
      return this.allowUser(this.api.user());
    }

    if (!this.api.hasToken()) {
      this.router.navigateByUrl('/');
      return false;
    }

    return this.api.me(!state.url.startsWith('/payment/success')).pipe(
      map(user => this.allowUser(user)),
      catchError(() => {
        this.api.logout();
        this.router.navigateByUrl('/');
        return of(false);
      })
    );
  }
  private allowUser(user: Me | null): boolean {
    if (user && !user.isAdmin && user.applicationInService) {
      this.router.navigateByUrl('/maintenance');
      return false;
    }
    return true;
  }
}
