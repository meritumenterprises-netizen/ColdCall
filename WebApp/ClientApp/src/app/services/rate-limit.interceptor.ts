import { Injectable } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { RateLimitToastService } from './rate-limit-toast.service';

@Injectable()
export class RateLimitInterceptor implements HttpInterceptor {
  constructor(private toast: RateLimitToastService) {}

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(request).pipe(catchError((error: HttpErrorResponse) => {
      if (error.status === 429 && /\/api(?:\/|\?|$)/i.test(request.url)) { this.toast.show(); }
      return throwError(error);
    }));
  }
}
