import { Injectable } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { EMPTY, Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { BlockedAccountService } from './blocked-account.service';

@Injectable()
export class BlockedAccountInterceptor implements HttpInterceptor {
  constructor(private blockedAccount: BlockedAccountService) {}

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const apiRequest = /\/api\//i.test(request.url);
    const logoutRequest = /\/api\/account\/(logout|csrf)(?:\?|$)/i.test(request.url);
    if (apiRequest && this.blockedAccount.blocked && !logoutRequest) { return EMPTY; }
    return next.handle(request).pipe(catchError((error: HttpErrorResponse) => {
      if (apiRequest && error.status === 403 && error.error && error.error.code === 'AccountDisabled') {
        this.blockedAccount.show();
        return EMPTY;
      }
      return throwError(error);
    }));
  }
}
