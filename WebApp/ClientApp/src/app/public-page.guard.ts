import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class PublicPageGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}

  canActivate(): boolean | UrlTree | Observable<boolean | UrlTree> {
    const destination = () => {
      const user = this.api.user();
      return user && !user.isAdmin && user.requiresProfileCompletion ? this.router.parseUrl('/profile/data') : true;
    };
    if (this.api.isAuthenticated()) { return destination(); }
    return this.api.restoreSession().pipe(map(() => destination()), catchError(() => of(true)));
  }
}
