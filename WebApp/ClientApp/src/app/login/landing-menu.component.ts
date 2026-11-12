import { ApiService } from '../services/api.service';
import { Component, ElementRef, HostListener, Input, ViewChild } from '@angular/core';
import guides from '../../assets/guides.json';

@Component({
  selector: 'app-landing-menu',
  templateUrl: './landing-menu.component.html',
  styleUrls: ['./landing-menu.component.css']
})
export class LandingMenuComponent {
  @Input() embedded = false;
  @Input() showBrand = true;
  contactOpen = false;
  guidesOpen = false;
  readonly guides = guides;
  @ViewChild('guidesButton', { static: false }) guidesButton: ElementRef;
  @ViewChild('contactButton', { static: true }) contactButton: ElementRef;
  constructor(private element: ElementRef, public api: ApiService) {}

  @HostListener('document:click', ['$event'])
  closeOutside(event: MouseEvent) {
    if (!this.element.nativeElement.contains(event.target)) { this.closeMenus(); }
  }

  closeMenus() { this.contactOpen = false; this.guidesOpen = false; }

  toggleGuides() { this.guidesOpen = !this.guidesOpen; this.contactOpen = false; }

  toggleContact() { this.contactOpen = !this.contactOpen; this.guidesOpen = false; }

  @HostListener('keydown.escape')
  closeWithEscape() {
    if (this.guidesOpen) { this.guidesOpen = false; this.guidesButton.nativeElement.focus(); }
    if (this.contactOpen) { this.contactOpen = false; this.contactButton.nativeElement.focus(); }
  }
}
