import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { catchError, map, shareReplay, switchMap, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface SortableColumn { key: string; label: string; }
export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; sortableColumns?: SortableColumn[]; }
export interface Pkd { id: number; code: string; name: string; }
export interface PkdClassification { status: string; text: string; pkdCodes: Pkd[]; }
export interface AddressOption { id: number; name: string; }
export type AddressLevel = 'wojewodztwa' | 'powiaty' | 'gminy' | 'miejscowosci';
export interface Company {
  id: number; name: string; businessTypeId: number; businessTypeName?: string; firstName?: string; lastName?: string; krs?: string; nip?: string;
  wojewodztwo?: string; city?: string; gmina?: string; powiat?: string; postalCode?: string; street?: string;
  buildingNumber?: string; apartmentNumber?: string; phone?: string; email?: string; googleEmail?: string;
  website?: string; instagramAccount?: string; facebookAccount?: string; startedAt?: string; createdOn?: string;
  regon?: string | null; statusDzialalnosciId: number; statusDzialalnosciName?: string | null;
  addressIsMissing: boolean | null; blockedByRODO: boolean;
  wojewodztwoId?: number | null; miejscowoscId?: number | null; gminaId?: number | null; powiatId?: number | null;
}
export interface CompanyAddress { companyAdresId: number; companyId: number; addressId: number; }
export interface CompanyAddressDetails extends CompanyAddress {
  wojewodztwoId: number; powiatId: number; gminaId: number; miejscowoscId: number;
  wojewodztwo: string; powiat: string; gmina: string; miejscowosc: string;
  ulica: string | null; nrUlicy: string | null; rodzaj: string | null;
  latitude: number | null; longitude: number | null; prngId: string | null;
}
export interface BusinessType { typDzialalnosciId: number; typDzialalnosci: string; }
export interface CompanyDetails { company: Company; pkdCodes: Pkd[]; addresses: CompanyAddressDetails[]; }
export interface CompanyFilters {
  companyName?: string;
  address: string; hasPhone: boolean | null; hasEmail: boolean | null; hasWebsite: boolean | null;
  startedFrom?: string; startedTo?: string; recordLimit: number;
  wojewodztwoId?: number; powiatId?: number; gminaId?: number; miejscowoscId?: number; radiusKm?: number;
  addressLocality?: boolean;
}
export interface CompanyShort {
  id: number; name: string; nip?: string; fullAddress: string;
  phone?: string; email?: string; www?: string; dataAktywnosci?: string;
  typyDzialalnosci?: string;
}
export interface SearchCriteria {
  wojewodztwoId?: number | null; powiatId?: number | null; gminaId?: number | null; miejscowoscId?: number | null;
  hasFirmyWPromieniu?: boolean | null; firmyWPromieniu?: number | null;
  companyName?: string;
  sortColumn: string; sortDirection: string; address: string;
  hasPhone: boolean | null; hasEmail: boolean | null; hasWebsite: boolean | null;
  dateRangeEnabled: boolean; dateFrom: string | null; dateTo: string | null; recordLimit: number;
}
export interface SearchProfile { id: number; name: string; pkdCodes: Pkd[]; criteria?: SearchCriteria; }
export interface Plan { code: string; searchLimit: number; descriptionHtml: string; price: number; currency: string; activeUntil?: string; order: number; canPurchase: boolean; }
export interface CurrentPlan { code: string; isTrial: boolean; descriptionHtml: string; maxNumberOfSearches: number; expiresAtUtc: string; isExpired?: boolean; }
export interface UserProfile { displayName: string | null; googleEmail: string; isAdmin: boolean; plan: CurrentPlan | null; }
export interface TrialOffer { code: string; descriptionHtml: string; expiresAtUtc: string; token: string; }
export interface Me { id: number; googleEmail: string; usesGmail?: boolean; displayName?: string; pictureUrl?: string; isAdmin: boolean; hasActivePlan: boolean; maxNumberOfExportedRecords: number; maxNumberOfSearches: number; plan?: CurrentPlan; trialOffer?: TrialOffer; }
export interface UserPlan { planCode: string; purchasedAtUtc: string; expiresAtUtc: string; source: string; descriptionHtml?: string; }
export interface AppUser { id: number; googleEmail: string; displayName?: string; plans: UserPlan[]; createdAtUtc?: string; }
export interface AuthConfig { googleClientId: string; }
export interface CsvExportRow { id: number; userId: number; searchProfileId: number; csvFileName: string; createdOn: string; numberOfRecords: number | null; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  private token = localStorage.getItem('google_id_token') || '';
  private authenticated = false;
  private currentUser: Me | null = null;
  private localSession = sessionStorage.getItem('coldcall_local_session') === '1';
  private csrfToken = '';
  private csrfRequest: Observable<string> | null = null;

  constructor(private http: HttpClient) {}

  setToken(token: string) {
    this.csrfToken = ''; this.csrfRequest = null;
    this.localSession = false;
    sessionStorage.removeItem('coldcall_local_session');
    this.token = token;
    this.authenticated = false;
    localStorage.setItem('google_id_token', token);
  }

  logout() {
    this.localSession = false;
    this.csrfToken = '';
    this.csrfRequest = null;
    sessionStorage.removeItem('coldcall_local_session');
    this.token = '';
    this.authenticated = false;
    this.currentUser = null;
    localStorage.removeItem('google_id_token');
  }

  isLoggedIn() { return !!this.token || this.localSession; }
  acceptTrial(token: string, termsAccepted: boolean) {
    return this.http.post(this.url('/api/auth/accept-trial'), { token, termsAccepted }, this.headers());
  }
  companyCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(this.url('/api/statistics/companies'));
  }
  hasToken() { return !!this.token || this.localSession; }
  isAuthenticated() { return this.authenticated; }
  isAdmin() { return this.authenticated && !!this.currentUser && this.currentUser.isAdmin; }
  isNonAdmin() { return this.authenticated && !!this.currentUser && !this.currentUser.isAdmin; }
  user() { return this.currentUser; }
  emailUrl(email: string): string {
    return this.currentUser && this.currentUser.usesGmail === false ? 'mailto:' + email :
      'https://mail.google.com/mail/?view=cm&fs=1&to=' + encodeURIComponent(email);
  }
  websiteHref(website: string): string {
    return /^https?:\/\//i.test(website) ? website : 'https://' + website;
  }

  formatPhone(value: string | null | undefined) {
    if (!value) { return ''; }
    const phone = value.trim();
    if (/^48\d{9}$/.test(phone)) { return phone.replace(/^48(\d{3})(\d{3})(\d{3})$/, '+48 $1 $2 $3'); }
    if (/^0048\d{9}$/.test(phone)) { return phone.replace(/^0048(\d{3})(\d{3})(\d{3})$/, '+48 $1 $2 $3'); }
    if (/^\+48\d{9}$/.test(phone)) { return phone.replace(/^(\+48)(\d{3})(\d{3})(\d{3})$/, '$1 $2 $3 $4'); }
    return /^\d{9}$/.test(phone) ? phone.replace(/(\d{3})(\d{3})(\d{3})/, '$1 $2 $3') : value;
  }

  headers() {
    let headers = new HttpHeaders();
    if (this.token) { headers = headers.set('Authorization', 'Bearer ' + this.token); }
    if (this.csrfToken) { headers = headers.set('X-CSRF-TOKEN', this.csrfToken); }
    return { headers, withCredentials: true };
  }

  private url(path: string) { return (environment.apiBaseUrl || '').replace(/\/$/, '') + path; }

  refreshCsrf(): Observable<string> {
    this.csrfRequest = this.http.get<{ token: string }>(this.url('/api/account/csrf'), this.headers()).pipe(
      map(response => response.token), tap(token => this.csrfToken = token),
      catchError(err => { this.csrfRequest = null; return throwError(err); }), shareReplay(1));
    return this.csrfRequest;
  }
  private accountPost<T>(path: string, body: any): Observable<T> {
    const token = this.csrfToken ? of(this.csrfToken) : this.csrfRequest || this.refreshCsrf();
    return token.pipe(switchMap(() => this.http.post<T>(this.url('/api/account/' + path), body, this.headers())));
  }
  registerLocal(displayName: string, email: string, password: string) {
    return this.accountPost<{ message: string }>('register', { displayName, email, password });
  }
  loginLocal(email: string, password: string) {
    this.csrfToken = ''; this.csrfRequest = null;
    this.authenticated = false; this.currentUser = null;
    this.token = ''; localStorage.removeItem('google_id_token');
    return this.accountPost<{ trialOffer?: TrialOffer }>('login', { email, password }).pipe(
      tap(() => { this.localSession = true; sessionStorage.setItem('coldcall_local_session', '1'); this.csrfToken = ''; this.csrfRequest = null; }),
      switchMap(result => this.refreshCsrf().pipe(map(() => result)))
    );
  }
  forgotPassword(email: string) { return this.accountPost<{ message: string }>('forgot-password', { email }); }
  resendConfirmation(email: string) { return this.accountPost<{ message: string }>('resend-confirmation', { email }); }
  confirmEmail(email: string, token: string) { return this.accountPost<{ message: string }>('confirm-email', { email, token }); }
  resetPassword(email: string, token: string, password: string) { return this.accountPost<{ message: string }>('reset-password', { email, token, password }); }
  logoutSession() { return this.accountPost<any>('logout', {}).pipe(tap(() => this.logout())); }

  authConfig(): Observable<AuthConfig> { return this.http.get<AuthConfig>(this.url('/auth-config.json')); }
  profile(): Observable<UserProfile> { return this.http.get<UserProfile>(this.url('/api/auth/profile'), this.headers()); }
  me(): Observable<Me> {
    const csrf = this.localSession && !this.csrfToken ? this.refreshCsrf() : of(this.csrfToken);
    return csrf.pipe(switchMap(() => this.http.get<Me>(this.url('/api/auth/me'), this.headers())),
      tap(user => {
        this.currentUser = user;
        this.authenticated = true;
      })
    );
  }
  pkd(term: string = ''): Observable<Pkd[]> { return this.http.get<Pkd[]>(this.url('/api/pkd'), { ...this.headers(), params: { term } }); }
  classifyPkd(description: string): Observable<PkdClassification> {
    return this.http.post<PkdClassification>(this.url('/api/pkd/classify'), { description }, this.headers());
  }
  addressOptions(level: AddressLevel, parentIds: (number | null)[] = []): Observable<AddressOption[]> {
    let params = new HttpParams();
    ['wojewodztwoId', 'powiatId', 'gminaId'].forEach((key, index) => {
      if (parentIds[index]) { params = params.set(key, String(parentIds[index])); }
    });
    return this.http.get<AddressOption[]>(this.url('/api/address/' + level), { ...this.headers(), params });
  }
  searches(): Observable<SearchProfile[]> { return this.http.get<SearchProfile[]>(this.url('/api/searches'), this.headers()); }
  distanceLocalities(term: string): Observable<AddressOption[]> {
    return this.http.get<AddressOption[]>(this.url('/api/address/distance-localities'), { ...this.headers(), params: { term } });
  }
  saveSearch(name: string, pkdCodeIds: number[], criteria?: SearchCriteria, profileId?: number): Observable<SearchProfile> {
    if (profileId != null) {
      return this.http.put<SearchProfile>(this.url('/api/searches/' + profileId), { name, pkdCodeIds, criteria }, this.headers());
    }
    return this.http.post<SearchProfile>(this.url('/api/searches'), { name, pkdCodeIds, criteria }, this.headers());
  }

  companies(page: number, sortBy: string, direction: string, pkdIds: number[], filters?: CompanyFilters, modifiedWithinDays?: number): Observable<PagedResult<CompanyShort>> {
    const params = this.companyParams(page, sortBy, direction, pkdIds, filters, modifiedWithinDays);
    return this.http.get<PagedResult<CompanyShort>>(this.url('/api/companies'), { ...this.headers(), params });
  }

  private companyParams(page: number, sortBy: string, direction: string, pkdIds: number[], filters?: CompanyFilters, modifiedWithinDays?: number): HttpParams {
    let params = new HttpParams().set('page', String(page)).set('pageSize', '10').set('sortBy', sortBy).set('direction', direction);
    if (modifiedWithinDays != null) { params = params.set('modifiedWithinDays', String(modifiedWithinDays)); }
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    if (filters) {
      if (filters.companyName && filters.companyName.trim().length >= 3) { params = params.set('companyName', filters.companyName.trim()); }
      if (filters.address.trim()) { params = params.set('address', filters.address.trim()); }
      ['wojewodztwoId', 'powiatId', 'gminaId', 'miejscowoscId', 'radiusKm', 'addressLocality'].forEach(key => {
        if (filters[key] !== undefined && filters[key] !== null) { params = params.set(key, String(filters[key])); }
      });
      ['hasPhone', 'hasEmail', 'hasWebsite'].forEach(key => {
        if (filters[key] !== null) { params = params.set(key, String(filters[key])); }
      });
      if (filters.startedFrom && filters.startedTo) {
        params = params.set('startedFrom', filters.startedFrom).set('startedTo', filters.startedTo);
      }
    }
    return params;
  }

  companyDetails(id: number): Observable<CompanyDetails> {
    return this.http.get<CompanyDetails>(this.url('/api/companies/' + id), this.headers());
  }
  companyNotes(id: number): Observable<{ notes: string }> {
    return this.http.get<{ notes: string }>(this.url('/api/companies/' + id + '/notes'), this.headers());
  }
  saveCompanyNotes(id: number, notes: string): Observable<{ notes: string }> {
    return this.http.put<{ notes: string }>(this.url('/api/companies/' + id + '/notes'), { notes }, this.headers());
  }

  exportCompanies(searchProfileId: number, modifiedWithinDays?: number, sortBy = 'dataAktywnosci', direction = 'desc', searchName?: string) {
    return this.http.post(this.url('/api/companies/export'), { searchProfileId, modifiedWithinDays, sortBy, direction, searchName },
      { ...this.headers(), responseType: 'blob', observe: 'response' });
  }

  checkCsvExport(searchProfileId: number): Observable<{ hash: string; exists: boolean }> {
    return this.http.post<{ hash: string; exists: boolean }>(
      this.url('/api/searches/' + searchProfileId + '/check-csv-export'), {}, this.headers());
  }

  csvExports(searchProfileId: number, sortBy: string, direction: string): Observable<CsvExportRow[]> {
    return this.http.get<CsvExportRow[]>(this.url('/api/companies/exports'), { ...this.headers(),
      params: { searchProfileId: String(searchProfileId), sortBy, direction } });
  }

  csvExportQuota(searchProfileId: number): Observable<{ used: number; limit: number | null; remaining: number | null }> {
    return this.http.get<{ used: number; limit: number | null; remaining: number | null }>(
      this.url('/api/companies/export-quota'), { ...this.headers(), params: { searchProfileId: String(searchProfileId) } });
  }

  csvDownloadUrl(row: CsvExportRow) {
    return this.url('/api/companies/DownloadCSV') + '?userId=' + row.userId + '&searchProfileId=' + row.searchProfileId
      + '&fileName=' + encodeURIComponent(row.csvFileName);
  }

  downloadCsv(row: CsvExportRow) {
    return this.http.get(this.csvDownloadUrl(row), { ...this.headers(), responseType: 'blob' });
  }

  plans(): Observable<Plan[]> { return this.http.get<Plan[]>(this.url('/api/plans'), this.headers()); }
  checkout(planCode: string) {
    return this.http.post<any>(this.url('/api/plans/checkout'), {
      planCode,
      successUrl: location.origin + '/plans?success=true',
      cancelUrl: location.origin + '/plans?canceled=true'
    }, this.headers());
  }

  adminCompanies(page: number, sortBy: string, direction: string, filter: string, pkdIds: number[] = [], filters?: CompanyFilters, nip = '', blockedByRODO: boolean | null = null): Observable<PagedResult<Company>> {
    let params = this.companyParams(page, sortBy, direction, pkdIds, filters).set('filter', filter).set('nip', nip);
    if (blockedByRODO !== null) { params = params.set('blockedByRODO', String(blockedByRODO)); }
    return this.http.get<PagedResult<Company>>(this.url('/api/admin/companies'), { ...this.headers(), params });
  }
  updateCompany(company: Company) { return this.http.put(this.url('/api/admin/companies/' + company.id), company, this.headers()); }
  updateCompanyRodo(id: number, blockedByRODO: boolean): Observable<{ blockedByRODO: boolean }> {
    return this.http.put<{ blockedByRODO: boolean }>(this.url('/api/admin/companies/' + id + '/rodo'), { blockedByRODO }, this.headers());
  }
  businessTypes(): Observable<BusinessType[]> { return this.http.get<BusinessType[]>(this.url('/api/admin/business-types'), this.headers()); }
  users(): Observable<AppUser[]> { return this.http.get<AppUser[]>(this.url('/api/admin/users'), this.headers()); }
  extendUserPlan(id: number) { return this.http.post(this.url('/api/admin/users/' + id + '/extend-plan'), {}, this.headers()); }
  deleteUserCsvExports(id: number): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(this.url('/api/admin/users/' + id + '/csv-exports'), this.headers());
  }
  grantPlan(userId: number, planCode: string) { return this.http.post(this.url('/api/admin/users/grant-plan'), { userId, planCode }, this.headers()); }
}
