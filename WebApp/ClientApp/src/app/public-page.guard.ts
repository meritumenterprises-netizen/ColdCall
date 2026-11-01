import { Injectable } from '@angular/core';
import { CanActivate } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map, timeout } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class PublicPageGuard implements CanActivate {
  constructor(private api: ApiService) {}

  canActivate(): boolean | Observable<boolean> {
    if (this.api.isAuthenticated()) { return true; }
    return this.api.restoreSession().pipe(timeout(10000), map(() => true), catchError(() => of(true)));
  }
}
