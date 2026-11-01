import { Component, ElementRef, HostListener, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-nav-menu',
  templateUrl: './nav-menu.component.html',
  styleUrls: ['./nav-menu.component.css']
})
export class NavMenuComponent {
  isExpanded = false;
  userMenuOpen = false;
  planHintOpen = false;
  failedAvatarUrl = '';

  get avatarUrl() {
    const user = this.api.user();
    const url = user && user.pictureUrl ? user.pictureUrl : '';
    return url !== this.failedAvatarUrl ? url : '';
  }

  get avatarInitials() {
    const user = this.api.user();
    const name = user ? user.displayName || user.googleEmail : '';
    return name.trim().split(/\s+/).slice(0, 2).map(part => part.charAt(0)).join('').toUpperCase();
  }

  avatarFailed() {
    const user = this.api.user();
    this.failedAvatarUrl = user && user.pictureUrl ? user.pictureUrl : '';
  }
  @ViewChild('userMenuButton', { static: false }) userMenuButton: ElementRef;

  @HostListener('document:click', ['$event'])
  closeUserMenu(event: MouseEvent) {
    if (!(event.target as HTMLElement).closest('.user-menu')) { this.userMenuOpen = false; }
  }

  @HostListener('document:keydown.escape')
  escapeUserMenu() {
    this.planHintOpen = false;
    if (this.userMenuOpen) {
      this.userMenuOpen = false;
      if (this.userMenuButton) { this.userMenuButton.nativeElement.focus(); }
    }
  }

  constructor(public api: ApiService, private router: Router) {}
  get isHomePage() { return this.router.url.split(/[?#]/)[0] === '/'; }
  get isMaintenancePage() { return this.router.url.split(/[?#]/)[0] === '/maintenance'; }

  get activePlan() {
    const user = this.api.user();
    return this.api.isAuthenticated() && user && user.hasActivePlan && user.plan &&
      this.planExpiration(user.plan.expiresAtUtc) > Date.now() ? user.plan : null;
  }

  private planExpiration(value: string) {
    return Date.parse(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z');
  }

  get planDaysRemaining() {
    const plan = this.activePlan;
    return plan ? Math.ceil((this.planExpiration(plan.expiresAtUtc) - Date.now()) / 86400000) : 0;
  }

  showAuthenticatedMenu() {
    return this.api.isAuthenticated() && this.router.url !== '/';
  }

  get showPlansLink() {
    const user = this.api.user();
    return this.api.isAuthenticated() && !!user && !user.isAdmin && !user.disablePlanPurchase;
  }

  collapse() {
    this.isExpanded = false;
  }

  toggle() {
    this.isExpanded = !this.isExpanded;
  }

  logout() {
    this.planHintOpen = false;
    this.userMenuOpen = false;
    this.api.logoutSession().subscribe(() => this.router.navigateByUrl('/'));
  }
}
