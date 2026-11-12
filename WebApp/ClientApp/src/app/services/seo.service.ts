import { DOCUMENT } from '@angular/common';
import { Inject, Injectable, OnDestroy } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { NavigationEnd, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import pages from '../../assets/seo-pages.json';

@Injectable({ providedIn: 'root' })
export class SeoService implements OnDestroy {
  private subscription: Subscription | null = null;
  constructor(private router: Router, private title: Title, private meta: Meta, @Inject(DOCUMENT) private document: Document) {}
  start() {
    if (this.subscription) { return; }
    this.subscription = this.router.events.pipe(filter(event => event instanceof NavigationEnd))
      .subscribe(() => this.update());
    this.update();
  }
  private update() {
    const path = this.router.url.split(/[?#]/)[0];
    const page = pages.find(item => item.path === path);
    const title = page ? page.title : 'FindMyClient.pl';
    const description = page ? page.description : '';
    const origin = 'https://www.findmyclient.pl';
    this.document.documentElement.lang = 'pl';
    this.title.setTitle(title);
    this.meta.updateTag({ name: 'description', content: description });
    this.meta.updateTag({ name: 'robots', content: page ? 'index,follow,max-image-preview:large' : 'noindex,nofollow' });
    for (const [property, content] of Object.entries({ 'og:title': title, 'og:description': description,
      'og:type': 'website', 'og:site_name': 'FindMyClient.pl', 'og:locale': 'pl_PL',
      'og:url': page ? origin + path : '', 'og:image': origin + '/assets/findmyclient-logo.png' })) {
      this.meta.updateTag({ property, content });
    }
    for (const [name, content] of Object.entries({ 'twitter:card': 'summary_large_image', 'twitter:title': title,
      'twitter:description': description, 'twitter:image': origin + '/assets/findmyclient-logo.png' })) {
      this.meta.updateTag({ name, content });
    }
    let canonical = this.document.querySelector('link[rel="canonical"]') as HTMLLinkElement;
    if (!page) { if (canonical) { canonical.remove(); } return; }
    if (!canonical) {
      canonical = this.document.createElement('link'); canonical.rel = 'canonical';
      this.document.head.appendChild(canonical);
    }
    canonical.href = origin + path;
  }
  ngOnDestroy() { if (this.subscription) { this.subscription.unsubscribe(); } }
}
