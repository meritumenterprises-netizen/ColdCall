import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class AdminGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}

  canActivate(): Observable<boolean | UrlTree> | UrlTree {
    if (!this.api.hasToken()) { return this.router.parseUrl('/'); }
    return this.api.me(false).pipe(
      map(user => user.isAdmin === true ? true
        : this.router.parseUrl(user.hasActivePlan ? '/dashboard' : '/profile/data')),
      catchError(() => of(this.router.parseUrl('/')))
    );
  }
}
