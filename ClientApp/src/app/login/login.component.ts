import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html'
})
export class LoginComponent {
  token = '';
  error = '';

  constructor(private api: ApiService, private router: Router) {}

  login() {
    this.error = '';
    if (!this.token.trim()) {
      this.error = 'Wklej Google ID token z aplikacji Google Authentication.';
      return;
    }
    this.api.setToken(this.token.trim());
    this.api.me().subscribe(me => {
      this.router.navigateByUrl(me.isAdmin ? '/admin' : (me.hasActivePlan ? '/dashboard' : '/plans'));
    }, () => {
      this.api.logout();
      this.error = 'Nie udalo sie potwierdzic tokena Google.';
    });
  }
}
