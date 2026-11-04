import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class PublicPlansGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}

  canActivate(): boolean | UrlTree | Observable<boolean | UrlTree> {
    const destination = () => this.api.isNonAdmin() ? this.router.parseUrl('/plans') : true;
    if (this.api.isAuthenticated()) { return destination(); }
    return this.api.restoreSession().pipe(map(() => destination()), catchError(() => of(true)));
  }
}
