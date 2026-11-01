import { Injectable } from '@angular/core';
import { CanActivate, Router, UrlTree } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

@Injectable({ providedIn: 'root' })
export class MaintenanceGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}
  canActivate(): boolean | UrlTree | Observable<boolean | UrlTree> {
    if (this.api.isAdmin()) { return this.router.parseUrl('/admin'); }
    if (this.api.isAuthenticated() || !this.api.hasToken()) { return true; }
    return this.api.me(false).pipe(
      map(user => user.isAdmin ? this.router.parseUrl('/admin') : true),
      catchError(() => of(this.router.parseUrl('/'))));
  }
}
