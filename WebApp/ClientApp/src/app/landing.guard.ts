import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class LandingGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}

  canActivate(route: ActivatedRouteSnapshot): boolean | Observable<boolean | UrlTree> {
    if (route.queryParams.mode === 'confirm-email' || route.queryParams.mode === 'reset-password') { return true; }
    return this.api.restoreSession().pipe(
      map(user => this.router.parseUrl(user.isAdmin ? '/admin' : user.applicationInService ? '/maintenance'
        : user.requiresProfileCompletion ? '/profile/data' : user.hasActivePlan ? '/dashboard' : '/plans')),
      catchError(() => {
        this.api.logout();
        return of(true);
      })
    );
  }
}
