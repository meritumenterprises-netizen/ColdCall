import { ApiService } from '../services/api.service';
import { Component, ElementRef, HostListener, Input, ViewChild } from '@angular/core';

@Component({
  selector: 'app-landing-menu',
  templateUrl: './landing-menu.component.html',
  styleUrls: ['./landing-menu.component.css']
})
export class LandingMenuComponent {
  @Input() embedded = false;
  @Input() showBrand = true;
  contactOpen = false;
  @ViewChild('contactButton', { static: true }) contactButton: ElementRef;
  constructor(private element: ElementRef, public api: ApiService) {}

  @HostListener('document:click', ['$event'])
  closeOutside(event: MouseEvent) {
    if (!this.element.nativeElement.contains(event.target)) { this.contactOpen = false; }
  }

  @HostListener('keydown.escape')
  closeWithEscape() {
    if (this.contactOpen) { this.contactOpen = false; this.contactButton.nativeElement.focus(); }
  }
}
