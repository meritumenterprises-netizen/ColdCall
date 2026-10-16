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
  @ViewChild('userMenuButton', { static: false }) userMenuButton: ElementRef;

  @HostListener('document:click', ['$event'])
  closeUserMenu(event: MouseEvent) {
    if (!(event.target as HTMLElement).closest('.user-menu')) { this.userMenuOpen = false; }
  }

  @HostListener('document:keydown.escape')
  escapeUserMenu() {
    if (this.userMenuOpen) {
      this.userMenuOpen = false;
      if (this.userMenuButton) { this.userMenuButton.nativeElement.focus(); }
    }
  }

  constructor(public api: ApiService, private router: Router) {}

  showAuthenticatedMenu() {
    return this.api.isAuthenticated() && this.router.url !== '/';
  }

  collapse() {
    this.isExpanded = false;
  }

  toggle() {
    this.isExpanded = !this.isExpanded;
  }

  logout() {
    this.userMenuOpen = false;
    this.api.logout();
    this.router.navigateByUrl('/');
  }
}
