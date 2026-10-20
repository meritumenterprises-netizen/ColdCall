import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild } from '@angular/core';
import { Subscription } from 'rxjs';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { finalize } from 'rxjs/operators';
import { ApiService, Company, CompanyDetails } from '../services/api.service';
import { isFutureActivityDate } from '../services/activity-date';

@Component({
  selector: 'app-company-details',
  templateUrl: './company-details.component.html',
  styleUrls: ['./company-details.component.css']
})
export class CompanyDetailsComponent implements AfterViewInit, OnDestroy {
  isFutureActivityDate = isFutureActivityDate;
  @Input() companyId: number;
  @Input() companyName = '';
  @Output() closed = new EventEmitter<void>();
  @Output() notesSaved = new EventEmitter<{ companyId: number; notes: string }>();
  @ViewChild('dialog', { static: true }) dialog: ElementRef;
  @ViewChild('copyConfirmation', { static: true }) copyConfirmation: ElementRef;
  @ViewChild('notesInput', { static: false }) notesInput: ElementRef;
  private notesFocusFrame: number | null = null;
  details: CompanyDetails | null = null;
  loading = false;
  error = '';
  copying = false;
  copyError = '';
  copyConfirmationMessage = '';
  @Input() activeTab: 'description' | 'notes' | 'map' = 'description';
  mapUrl: SafeResourceUrl | null = null;
  notes = '';
  private savedNotes = '';
  notesLoading = false;
  notesSaving = false;
  notesError = '';
  notesLoaded = false;
  private notesRequest: Subscription | null = null;
  private saveNotesRequest: Subscription | null = null;

  get notesChanged() { return this.notes !== this.savedNotes; }

  get dialogTitle(): string {
    if (!this.details) { return 'Dzia\u0142alno\u015b\u0107'; }
    const company = this.details.company;
    const civilPartnershipStatus = 'Dzia\u0142alno\u015b\u0107 prowadzona wy\u0142\u0105cznie w formie sp\u00f3\u0142ki cywilnej';
    return (company.statusDzialalnosciName || '').trim().toLowerCase() === civilPartnershipStatus.toLowerCase()
      ? civilPartnershipStatus : company.businessTypeName || 'Dzia\u0142alno\u015b\u0107';
  }

  get activityDateKey(): 'startedAt' | 'resumedOn' | 'suspendedOn' {
    if (this.details && this.details.company.statusDzialalnosciId === 2) { return 'suspendedOn'; }
    return this.details && this.details.company.resumedOn != null ? 'resumedOn' : 'startedAt';
  }

  get activityDateLabel(): string {
    if (this.activityDateKey === 'suspendedOn') { return 'Zawieszono dnia'; }
    return this.activityDateKey === 'resumedOn'
      ? 'Dzia\u0142alno\u015b\u0107 wznowiona od' : 'Dzia\u0142alno\u015b\u0107 aktywna od';
  }

  navigateTabs(event: KeyboardEvent) {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].indexOf(event.key) < 0) { return; }
    event.preventDefault();
    const tabs: ('description' | 'notes' | 'map')[] = ['description', 'map', 'notes'];
    const index = tabs.indexOf(this.activeTab);
    this.activeTab = event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1] :
      tabs[(index + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
    this.dialog.nativeElement.querySelector('#company-' + this.activeTab + '-tab').focus();
  }
  private openCeidgAfterConfirmation = false;
  private request: Subscription | null = null;
  readonly sections: { id: string; label: string; rows: { key: keyof Company; label: string }[][] }[] = [
    { id: 'owner', label: 'W\u0142a\u015bciciel', rows: [[
      { key: 'firstName', label: 'Imi\u0119' }, { key: 'lastName', label: 'Nazwisko' },
      { key: 'regon', label: 'REGON' }, { key: 'nip', label: 'NIP' },
      { key: 'krs', label: 'KRS' }
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
    ]] }
  ];

  constructor(private api: ApiService, private sanitizer: DomSanitizer) {}

  ngAfterViewInit() {
    this.dialog.nativeElement.showModal();
    // Defer state changes until the initial view check is complete.
    Promise.resolve().then(() => this.load());
  }

  load() {
    if (this.request) { this.request.unsubscribe(); }
    this.loading = true;
    this.error = '';
    this.mapUrl = null;
    this.request = this.api.companyDetails(this.companyId)
      .pipe(finalize(() => this.loading = false))
      .subscribe(details => {
        this.details = details;
        const company = details.company;
        if ((company.city || '').trim() || (company.street || '').trim()) {
          const streetAddress = [company.street, company.buildingNumber]
            .filter(part => part && part.trim()).map(part => part!.trim()).join(' ');
          const address = [streetAddress, company.postalCode,
            company.city, company.gmina, company.powiat, company.wojewodztwo, 'Polska']
            .filter(part => part && part.trim()).map(part => part!.trim()).join(', ');
          // Only the fixed Google host is trusted; address data is URL-encoded.
          this.mapUrl = this.sanitizer.bypassSecurityTrustResourceUrl(
            'https://www.google.com/maps?q=' + encodeURIComponent(address) + '&output=embed');
        }
        this.loadNotes();
      }, err => {
        this.error = err.status === 404 ? 'Nie znaleziono firmy.' :
          err.status === 402 ? 'Plan wygas\u0142.' : 'Nie uda\u0142o si\u0119 pobra\u0107 szczeg\u00f3\u0142\u00f3w firmy.';
      });
  }

  close() { this.closed.emit(); }

  openNotesTab() {
    this.activeTab = 'notes';
    this.focusNotesAtEnd();
  }

  private focusNotesAtEnd() {
    if (this.notesFocusFrame !== null) { cancelAnimationFrame(this.notesFocusFrame); }
    this.notesFocusFrame = requestAnimationFrame(() => {
      this.notesFocusFrame = null;
      if (this.activeTab !== 'notes' || !this.notesLoaded || this.notesLoading || !this.notesInput) { return; }
      const input = this.notesInput.nativeElement as HTMLTextAreaElement;
      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
      input.scrollTop = input.scrollHeight;
    });
  }

  loadNotes() {
    if (this.notesRequest) { this.notesRequest.unsubscribe(); }
    this.notesLoading = true;
    this.notesLoaded = false;
    this.notesError = '';
    this.notesRequest = this.api.companyNotes(this.companyId).pipe(finalize(() => this.notesLoading = false))
      .subscribe(result => {
        this.notes = result.notes;
        this.savedNotes = result.notes;
        this.notesLoaded = true;
        this.focusNotesAtEnd();
      }, () => this.notesError = 'Nie udalo sie wczytac notatek.');
  }

  saveNotes() {
    if (!this.notesChanged || !this.notesLoaded || this.notesSaving || this.notesLoading) { return; }
    const notes = this.notes;
    this.notesSaving = true;
    this.notesError = '';
    this.saveNotesRequest = this.api.saveCompanyNotes(this.companyId, notes).pipe(finalize(() => this.notesSaving = false))
      .subscribe(() => {
        this.savedNotes = notes;
        this.notesSaved.emit({ companyId: this.companyId, notes });
        this.close();
      }, () => this.notesError = 'Nie udalo sie zapisac notatek.');
  }

  async copyCompany() {
    if (!this.details || this.loading || this.copying) { return; }
    const fields: { key: keyof Company; label: string }[] = [
      { key: 'name', label: 'Nazwa' }, { key: 'businessTypeName', label: 'Typ dzia\u0142alno\u015bci' },
      { key: this.activityDateKey, label: this.activityDateLabel },
      { key: 'dataModifiedOn', label: 'Zmodyfikowano' }
    ];
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
      this.copyConfirmationMessage = 'Skopiowano dane firmy do schowka';
      this.openCeidgAfterConfirmation = false;
      this.copyConfirmation.nativeElement.showModal();
    } catch {
      this.copyError = 'Nie uda\u0142o si\u0119 skopiowa\u0107 danych firmy do schowka.';
    } finally {
      this.copying = false;
    }
  }

  closeCopyConfirmation() {
    this.openCeidgAfterConfirmation = false;
    this.copyConfirmation.nativeElement.close();
  }

  confirmCopy() {
    const openCeidg = this.openCeidgAfterConfirmation;
    this.closeCopyConfirmation();
    if (openCeidg) {
      window.open('https://aplikacja.ceidg.gov.pl/ceidg/ceidg.public.ui/search.aspx', '_blank', 'noopener,noreferrer');
    }
  }

  async copyNipAndOpenCeidg() {
    if (!this.details || this.loading || this.copying || !(this.details.company.nip || '').trim()) { return; }
    this.copying = true;
    this.copyError = '';
    try {
      await navigator.clipboard.writeText(this.details.company.nip!.trim());
      this.copyConfirmationMessage = 'Skopiowano NIP do schowka. Otwieram stron\u0119 CEIDG';
      this.openCeidgAfterConfirmation = true;
      this.copyConfirmation.nativeElement.showModal();
    } catch {
      this.copyError = 'Nie uda\u0142o si\u0119 skopiowa\u0107 NIP do schowka.';
    } finally {
      this.copying = false;
    }
  }

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

  get emailUrl(): string {
    const email = this.details ? this.details.company.email || '' : '';
    return this.api.emailUrl(email);
  }
  websiteHref(website: string) { return this.api.websiteHref(website); }

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
    if ((key === 'startedAt' || key === 'resumedOn' || key === 'suspendedOn' || key === 'dataModifiedOn') && value) { return String(value).substring(0, 10); }
    return value == null || value === '' ? '\u2014' : String(value);
  }

  ngOnDestroy() {
    if (this.notesFocusFrame !== null) { cancelAnimationFrame(this.notesFocusFrame); }
    if (this.notesRequest) { this.notesRequest.unsubscribe(); }
    if (this.saveNotesRequest) { this.saveNotesRequest.unsubscribe(); }
    if (this.request) { this.request.unsubscribe(); }
    this.dialog.nativeElement.close();
    this.copyConfirmation.nativeElement.close();
  }
}
