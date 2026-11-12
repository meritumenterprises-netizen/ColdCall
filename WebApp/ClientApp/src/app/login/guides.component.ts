import { Component } from '@angular/core';
import { Router } from '@angular/router';
import catalog from '../../assets/guides.json';

@Component({
  selector: 'app-guides',
  templateUrl: './guides.component.html',
  styleUrls: ['./guides.component.css']
})
export class GuidesComponent {
  readonly guides = catalog;
  constructor(private router: Router) {}
  get article() {
    const path = this.router.url.split(/[?#]/)[0];
    return this.guides.find(guide => guide.path === path);
  }
}
