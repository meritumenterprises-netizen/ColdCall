import { Component, Input, OnDestroy, ViewChild } from '@angular/core';
import { NgForm } from '@angular/forms';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ApiService, FeedbackRequest } from '../services/api.service';
import { TurnstileComponent } from './turnstile.component';

@Component({
  selector: 'app-feedback',
  templateUrl: './feedback.component.html',
  styles: [`.feedback-form { max-width: 720px; }
    fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
    label { display: block; margin-bottom: 6px; }
    input, textarea { display: block; width: 100%; box-sizing: border-box; margin-bottom: 18px; }
    textarea { resize: vertical; line-height: 1.5; }
    .feedback-actions { display: flex; gap: 12px; align-items: center; }`]
})
export class FeedbackComponent implements OnDestroy {
  @Input() support = false;
  @Input() rodo = false;
  @ViewChild(TurnstileComponent, { static: false }) turnstile: TurnstileComponent;
  turnstileToken = '';
  details = { firstName: '', lastName: '', email: '', comment: '', nip: '' };
  sending = false;
  error = '';
  sent = false;
  private request: Subscription | null = null;
  constructor(private api: ApiService) {}
  get complete() {
    return [this.details.firstName, this.details.lastName, this.details.email, this.details.comment]
      .every(value => !!value.trim()) && (!this.rodo || /^[0-9]{10}$/.test(this.details.nip));
  }
  submit(form: NgForm) {
    if (this.sent || this.sending || form.invalid || !this.complete || !this.turnstileToken) { return; }
    this.sending = true; this.error = '';
    const details: FeedbackRequest = { firstName: this.details.firstName.trim(), lastName: this.details.lastName.trim(),
      email: this.details.email.trim(), comment: this.details.comment.trim(), turnstileToken: this.turnstileToken, nip: this.details.nip.trim() };
    const request = this.rodo ? this.api.sendRodo(details)
      : this.support ? this.api.sendSupport(details) : this.api.sendFeedback(details);
    this.request = request.pipe(finalize(() => {
      this.sending = false;
      this.turnstileToken = '';
      if (!this.sent && this.turnstile) { this.turnstile.reset(); }
    })).subscribe(() => {
      this.sent = true;
      this.details = { firstName: '', lastName: '', email: '', comment: '', nip: '' };
      form.resetForm(this.details);
    }, err => {
      const errors = err.error && err.error.errors;
      this.error = err.error && err.error.message ? err.error.message
        : err.status === 429 ? 'Zbyt wiele wiadomości. Spróbuj ponownie za minutę.'
        : errors ? Object.keys(errors).map(key => errors[key].join(' ')).join(' ')
        : 'Nie udało się wysłać wiadomości. Spróbuj ponownie później.';
    });
  }
  ngOnDestroy() { if (this.request) { this.request.unsubscribe(); } }
}
