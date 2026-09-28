import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
  constructor(private api: ApiService, private router: Router) {}

  canActivate(): boolean | Observable<boolean> {
    if (this.api.isAuthenticated()) {
      return true;
    }

    if (!this.api.hasToken()) {
      this.router.navigateByUrl('/');
      return false;
    }

    return this.api.me().pipe(
      map(() => true),
      catchError(() => {
        this.api.logout();
        this.router.navigateByUrl('/');
        return of(false);
      })
    );
  }
}
