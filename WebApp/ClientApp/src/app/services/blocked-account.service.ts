import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class BlockedAccountService {
  readonly notice = new BehaviorSubject<boolean>(false);
  get blocked() { return this.notice.value; }
  show() { if (!this.blocked) { this.notice.next(true); } }
  clear() { this.notice.next(false); }
}
