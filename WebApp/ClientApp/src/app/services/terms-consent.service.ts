import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, ReplaySubject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class TermsConsentService {
  readonly visible = new BehaviorSubject<boolean>(false);
  private decision: ReplaySubject<void> | null = null;

  requireConsent(): Observable<void> {
    if (!this.decision) {
      this.decision = new ReplaySubject<void>(1);
      this.visible.next(true);
    }
    return this.decision.asObservable();
  }
  accept() {
    const decision = this.decision;
    this.decision = null;
    this.visible.next(false);
    if (decision) { decision.next(); decision.complete(); }
  }
  decline() {
    const decision = this.decision;
    this.decision = null;
    this.visible.next(false);
    if (decision) {
      decision.error({ status: 403, error: { code: 'TermsDeclined', message: 'Odm\u00f3wiono warunk\u00f3w u\u017cycia serwisu.' } });
    }
  }
}
