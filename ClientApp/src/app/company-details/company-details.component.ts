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
  details: CompanyDetails | null = null;
  loading = false;
  error = '';
  private request: Subscription | null = null;
  readonly fields: { key: keyof Company; label: string }[] = [
    { key: 'nip', label: 'NIP' }, { key: 'krs', label: 'KRS' },
    { key: 'firstName', label: 'Imi\u0119' }, { key: 'lastName', label: 'Nazwisko' },
    { key: 'businessTypeName', label: 'Typ dzia\u0142alno\u015bci' },
    { key: 'startedAt', label: 'Data aktywno\u015bci' },
    { key: 'createdOn', label: 'Data utworzenia' },
    { key: 'wojewodztwo', label: 'Wojew\u00f3dztwo' }, { key: 'powiat', label: 'Powiat' },
    { key: 'gmina', label: 'Gmina' }, { key: 'city', label: 'Miejscowo\u015b\u0107' },
    { key: 'postalCode', label: 'Kod pocztowy' }, { key: 'street', label: 'Ulica' },
    { key: 'buildingNumber', label: 'Numer budynku' }, { key: 'apartmentNumber', label: 'Numer lokalu' },
    { key: 'phone', label: 'Telefon' }, { key: 'email', label: 'Email' },
    { key: 'googleEmail', label: 'Email Google' }, { key: 'website', label: 'Witryna WWW' },
    { key: 'instagramAccount', label: 'Instagram' }, { key: 'facebookAccount', label: 'Facebook' }
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

  value(key: keyof Company): string {
    if (!this.details) { return ''; }
    const value = this.details.company[key];
    if (key === 'startedAt' && value) { return String(value).substring(0, 10); }
    if (key === 'createdOn' && value) { return String(value).replace('T', ' ').substring(0, 19); }
    return value == null || value === '' ? '\u2014' : String(value);
  }

  ngOnDestroy() {
    if (this.request) { this.request.unsubscribe(); }
    this.dialog.nativeElement.close();
  }
}
