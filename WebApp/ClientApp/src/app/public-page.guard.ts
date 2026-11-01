import { Injectable } from '@angular/core';
import { CanActivate } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ApiService } from './services/api.service';

@Injectable({ providedIn: 'root' })
export class PublicPageGuard implements CanActivate {
  constructor(private api: ApiService) {}

  canActivate(): boolean | Observable<boolean> {
    if (this.api.isAuthenticated()) { return true; }
    return this.api.restoreSession().pipe(map(() => true), catchError(() => of(true)));
  }
}
