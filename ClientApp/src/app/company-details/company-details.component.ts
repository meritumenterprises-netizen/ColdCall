import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ApiService, Company, CompanyDetails } from '../services/api.service';

@Component({
  selector: 'app-company-details',
  templateUrl: './company-details.component.html',
  styleUrls: ['./company-details.component.css']
})
export class CompanyDetailsComponent implements AfterViewInit, OnDestroy {
  @Input() companyId: number;
  @Input() companyName = '';
  @Output() closed = new EventEmitter<void>();
  @ViewChild('dialog', { static: true }) dialog: ElementRef;
  @ViewChild('copyConfirmation', { static: true }) copyConfirmation: ElementRef;
  details: CompanyDetails | null = null;
  loading = false;
  error = '';
  copying = false;
  copyError = '';
  private request: Subscription | null = null;
  readonly sections: { id: string; label: string; rows: { key: keyof Company; label: string }[][] }[] = [
    { id: 'owner', label: 'W\u0142a\u015bciciel', rows: [[
      { key: 'firstName', label: 'Imi\u0119' }, { key: 'lastName', label: 'Nazwisko' },
      { key: 'regon', label: 'REGON' }, { key: 'nip', label: 'NIP' },
      { key: 'startedAt', label: 'Aktywne od' }
    ]] },
    { id: 'address', label: 'Adres', rows: [[
      { key: 'wojewodztwo', label: 'Wojew\u00f3dztwo' }, { key: 'powiat', label: 'Powiat' },
      { key: 'gmina', label: 'Gmina' }, { key: 'city', label: 'Miejscowo\u015b\u0107' },
      { key: 'postalCode', label: 'Kod Pocztowy' }
    ], [
      { key: 'street', label: 'Ulica' },
      { key: 'buildingNumber', label: 'Numer budynku' }, { key: 'apartmentNumber', label: 'Nr lokalu' }
    ]] },
    { id: 'contact', label: 'Kontakt', rows: [[
      { key: 'email', label: 'Email' }, { key: 'phone', label: 'Telefon' }, { key: 'website', label: 'Witryna WWW' }
    ], [
      { key: 'googleEmail', label: 'Email Google' },
      { key: 'instagramAccount', label: 'Instagram' }, { key: 'facebookAccount', label: 'Facebook' }
    ]] }
  ];

  constructor(private api: ApiService) {}

  ngAfterViewInit() {
    this.dialog.nativeElement.showModal();
    // Defer state changes until the initial view check is complete.
    Promise.resolve().then(() => this.load());
  }

  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.request = this.api.companyDetails(this.companyId)
      .pipe(finalize(() => this.loading = false))
      .subscribe(details => this.details = details, err => {
        this.error = err.status === 404 ? 'Nie znaleziono firmy.' :
          err.status === 402 ? 'Plan wygas\u0142.' : 'Nie uda\u0142o si\u0119 pobra\u0107 szczeg\u00f3\u0142\u00f3w firmy.';
      });
  }

  close() { this.closed.emit(); }

  async copyCompany() {
    if (!this.details || this.loading || this.copying) { return; }
    const fields: { key: keyof Company; label: string }[] = [{ key: 'name', label: 'Nazwa' }];
    this.sections.forEach(section => section.rows.forEach(row => fields.push(...row)));
    const lines = fields.map(field => field.label + ': ' + this.value(field.key).replace(/[\r\n]+/g, ' '));
    if (this.details.pkdCodes.length) {
      lines.push('');
      this.details.pkdCodes.forEach(pkd => lines.push(pkd.code + ': ' + pkd.name.replace(/[\r\n]+/g, ' ')));
    }
    this.copying = true;
    this.copyError = '';
    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      this.copyConfirmation.nativeElement.showModal();
    } catch {
      this.copyError = 'Nie uda\u0142o si\u0119 skopiowa\u0107 danych firmy do schowka.';
    } finally {
      this.copying = false;
    }
  }

  closeCopyConfirmation() { this.copyConfirmation.nativeElement.close(); }

  openGoogle(includeCompanyName = true) {
    if (!this.details || this.loading) { return; }
    const company = this.details.company;
    const query = [includeCompanyName ? company.name : null, company.city, company.street, company.buildingNumber, company.apartmentNumber]
      .filter(part => part && part.trim()).map(part => part!.trim()).join(' ');
    if (!query) { return; }
    window.open('https://local.google.com/?q=' + encodeURIComponent(query), '_blank', 'noopener,noreferrer');
  }

  get websiteUrl(): string | null {
    if (!this.details || this.loading) { return null; }
    const website = (this.details.company.website || '').trim();
    if (!website) { return null; }
    const candidate = website.startsWith('//') ? 'https:' + website :
      /^[a-z][a-z\d+.-]*:/i.test(website) ? website : 'https://' + website;
    try {
      const url = new URL(candidate);
      return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null;
    } catch { return null; }
  }

  openWebsite() {
    const url = this.websiteUrl;
    if (url) { window.open(url, '_blank', 'noopener,noreferrer'); }
  }

  value(key: keyof Company): string {
    if (!this.details) { return ''; }
    const value = this.details.company[key];
    if (key === 'phone' && value) {
      const phone = String(value).trim().replace(/^\*\*(\d{9})\*\*$/, '$1');
      if (/^48\d{9}$/.test(phone)) { return phone.replace(/^48(\d{3})(\d{3})(\d{3})$/, '+48 $1 $2 $3'); }
      if (/^0048\d{9}$/.test(phone)) { return phone.replace(/^0048(\d{3})(\d{3})(\d{3})$/, '+48 $1 $2 $3'); }
      if (/^\+48\d{9}$/.test(phone)) { return phone.replace(/^(\+48)(\d{3})(\d{3})(\d{3})$/, '$1 $2 $3 $4'); }
      if (/^\d{9}$/.test(phone)) { return phone.replace(/(\d{3})(\d{3})(\d{3})/, '$1 $2 $3'); }
    }
    if (key === 'startedAt' && value) { return String(value).substring(0, 10); }
    return value == null || value === '' ? '\u2014' : String(value);
  }

  ngOnDestroy() {
    if (this.request) { this.request.unsubscribe(); }
    this.dialog.nativeElement.close();
    this.copyConfirmation.nativeElement.close();
  }
}
