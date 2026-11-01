import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { catchError, map, shareReplay, switchMap, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { TermsConsentService } from './terms-consent.service';

export interface SortableColumn { key: string; label: string; }
export interface AdminUserLogin { userLoginId: number; userId: number; userName: string | null; email: string | null; loginDate: string; }
export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; sortableColumns?: SortableColumn[]; totalWithoutLimit?: number; hasNotes?: boolean; }
export interface Pkd { id: number; code: string; name: string; }
export interface PkdClassification { status: string; text: string; pkdCodes: Pkd[]; }
export interface AddressOption { id: number; name: string; }
export interface AdminImport {
  importId: number;
  importDate: string;
  importTargetRecords: number | null;
  startedAt: string;
  finishedAt: string | null;
  duration: string | null;
}

export interface AdminPlanDefinition {
  id: number; code: string; maxNumberOfSearches: number; maxNumberOfCSVExportsPerSearch: number | null;
  maxNumberOfExportedRecords: number | null; expiresAfterDays: number | null; showInOffer: boolean; enableCSVDeleting: boolean;
  price: number; descriptionHtml: string; isUpgrade: boolean; isTrial: boolean;
}
export type AddressLevel = 'wojewodztwa' | 'powiaty' | 'gminy' | 'miejscowosci';
export interface Company {
  addressSort?: string | null;
  userNotes?: string | null;
  id: number; name: string; businessTypeId: number; businessTypeName?: string; firstName?: string; lastName?: string; krs?: string; nip?: string;
  wojewodztwo?: string; city?: string; gmina?: string; powiat?: string; postalCode?: string; street?: string;
  buildingNumber?: string; apartmentNumber?: string; phone?: string; email?: string; googleEmail?: string;
  website?: string; instagramAccount?: string; facebookAccount?: string; startedAt?: string; createdOn?: string; dataModifiedOn?: string | null;
  resumedOn?: string | null;
  suspendedOn?: string | null;
  regon?: string | null; statusDzialalnosciId: number; statusDzialalnosciName?: string | null;
  addressIsMissing: boolean | null; blockedByRODO: boolean;
  wojewodztwoId?: number | null; miejscowoscId?: number | null; gminaId?: number | null; powiatId?: number | null;
}
export interface AdminGlobalSetting { globalSettingsId: number; globalDegreeOfParallelismImportByDate: number; applicationInService: boolean; }
export interface AdminUserPayment { planCode: string; paidAtUtc: string; amountPaid: number; }
export interface CompanyAddress { companyAdresId: number; companyId: number; addressId: number; }
export interface CompanyAddressDetails extends CompanyAddress {
  wojewodztwoId: number; powiatId: number; gminaId: number; miejscowoscId: number;
  wojewodztwo: string; powiat: string; gmina: string; miejscowosc: string;
  ulica: string | null; nrUlicy: string | null; rodzaj: string | null;
  latitude: number | null; longitude: number | null; prngId: string | null;
}
export interface BusinessType { typDzialalnosciId: number; typDzialalnosci: string; }
export interface CompanyPkd extends Pkd { prevailing: boolean; }
export interface CompanyDetails { company: Company; pkdCodes: CompanyPkd[]; addresses: CompanyAddressDetails[]; }
export interface CompanyFilters {
  companyStatusIds?: number[];
  companyName?: string;
  address: string; hasPhone: boolean | null; hasEmail: boolean | null; hasWebsite: boolean | null;
  startedFrom?: string; startedTo?: string; recordLimit: number;
  wojewodztwoId?: number; powiatId?: number; gminaId?: number; miejscowoscId?: number; radiusKm?: number;
  addressLocality?: boolean;
}
export interface CompanyShort {
  userNotes?: string | null;
  id: number; name: string; nip?: string; fullAddress: string;
  phone?: string; email?: string; www?: string; dataAktywnosci?: string; dataModifiedOn?: string | null;
  typyDzialalnosci?: string;
}
export interface SearchCriteria {
  companyStatusIds?: number[];
  wojewodztwoId?: number | null; powiatId?: number | null; gminaId?: number | null; miejscowoscId?: number | null;
  hasFirmyWPromieniu?: boolean | null; firmyWPromieniu?: number | null;
  companyName?: string;
  sortColumn: string; sortDirection: string; address: string;
  hasPhone: boolean | null; hasEmail: boolean | null; hasWebsite: boolean | null;
  dateRangeEnabled: boolean; dateFrom: string | null; dateTo: string | null; recordLimit: number;
}
export interface SearchProfile { id: number; name: string; pkdCodes: Pkd[]; criteria?: SearchCriteria; hasCsvExports?: boolean; }
export interface Plan { code: string; searchLimit: number; descriptionHtml: string; price: number; currency: string; activeUntil?: string; order: number; canPurchase: boolean; }
export interface CurrentPlan { code: string; isTrial: boolean; descriptionHtml: string; maxNumberOfSearches: number; enableCSVDeleting: boolean; expiresAtUtc: string; isExpired?: boolean; }
export interface ProfileDetails { firstName: string; lastName: string; companyName: string; nip: string; postalCode: string; city: string; street: string; streetNumber: string; }
export interface UserProfile extends ProfileDetails { displayName: string | null; googleEmail: string; isAdmin: boolean; plan: CurrentPlan | null; }
export interface StripePurchase {
  planCode: string; maxNumberOfSearches: number; maxNumberOfCSVExportsPerSearch: number | null;
  maxNumberOfExportedRecords: number | null; amountPaid: number; paidAtUtc: string;
}
export interface TrialOffer { code: string; descriptionHtml: string; expiresAtUtc: string; token: string; }
export interface Me { id: number; googleEmail: string; usesGmail?: boolean; disablePlanPurchase?: boolean; displayName?: string; pictureUrl?: string; isAdmin: boolean; termsOfServiceAccepted?: boolean | null; applicationInService?: boolean; hasActivePlan: boolean; hasExpiredTrial?: boolean; hasExpiredPlan?: boolean; maxNumberOfExportedRecords: number; maxNumberOfSearches: number; plan?: CurrentPlan; trialOffer?: TrialOffer; }
export interface UserPlan { planCode: string; purchasedAtUtc: string; expiresAtUtc: string; source: string | null; descriptionHtml?: string; }
export interface PlanDefinitionOption { id: number; code: string; descriptionHtml: string; }
export interface AppUser { id: number; googleEmail: string; displayName?: string; plans: UserPlan[]; createdAtUtc?: string; disablePlanPurchase?: boolean; isEnabled?: boolean;
  firstName?: string | null; lastName?: string | null; companyName?: string | null; nip?: string | null; postalCode?: string | null;
  isAdmin?: boolean;
  city?: string | null; street?: string | null; streetNumber?: string | null;
}
export interface AuthConfig { googleClientId: string; }
export interface CsvExportRow { id: number; userId: number; searchProfileId: number; csvFileName: string; createdOn: string; numberOfRecords: number | null; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  private purchasedPlan: CurrentPlan | null = null;

  rememberPurchasedPlan() {
    this.purchasedPlan = this.currentUser && this.currentUser.plan ? { ...this.currentUser.plan } : null;
  }

  takePurchasedPlan(): CurrentPlan | null {
    const plan = this.purchasedPlan;
    this.purchasedPlan = null;
    return plan;
  }
  private expiredTrialNotified = false;
  readonly expiredTrialNotice = new BehaviorSubject<boolean>(false);

  acknowledgeExpiredTrial() { this.expiredTrialNotice.next(false); }
  private token = '';
  private authenticated = false;
  private adminMaintenanceNotice = false;
  takeAdminMaintenanceNotice(): boolean {
    const show = this.adminMaintenanceNotice;
    this.adminMaintenanceNotice = false;
    return show;
  }
  private currentUser: Me | null = null;
  private localSession = Number(localStorage.getItem('coldcall_session_expires')) > Date.now();
  private csrfToken = '';
  private csrfRequest: Observable<string> | null = null;

  constructor(private http: HttpClient, private termsConsent: TermsConsentService) {
    localStorage.removeItem('google_id_token');
    sessionStorage.removeItem('coldcall_local_session');
  }

  private rememberSession(expiresAtUtc: string) {
    this.token = '';
    this.localSession = true;
    localStorage.setItem('coldcall_session_expires', String(Date.parse(expiresAtUtc)));
    this.csrfToken = ''; this.csrfRequest = null;
  }

  startGoogleSession() {
    return this.http.post<{ sessionExpiresAtUtc: string }>(this.url('/api/auth/session'), {}, this.headers()).pipe(
      tap(result => this.rememberSession(result.sessionExpiresAtUtc)),
      switchMap(() => this.refreshCsrf())
    );
  }

  restoreSession(): Observable<Me> {
    return this.http.get<{ sessionExpiresAtUtc: string }>(this.url('/api/account/session'), this.headers()).pipe(
      tap(result => this.rememberSession(result.sessionExpiresAtUtc)),
      switchMap(() => this.me())
    );
  }

  setToken(token: string) {
    this.purchasedPlan = null;
    this.expiredTrialNotified = false;
    this.expiredTrialNotice.next(false);
    this.csrfToken = ''; this.csrfRequest = null;
    this.localSession = false;
    sessionStorage.removeItem('coldcall_local_session');
    this.token = token;
    this.authenticated = false;
    localStorage.removeItem('coldcall_session_expires');
  }

  logout() {
    this.adminMaintenanceNotice = false;
    localStorage.removeItem('coldcall_session_expires');
    this.purchasedPlan = null;
    this.expiredTrialNotified = false;
    this.expiredTrialNotice.next(false);
    this.localSession = false;
    this.csrfToken = '';
    this.csrfRequest = null;
    sessionStorage.removeItem('coldcall_local_session');
    this.token = '';
    this.authenticated = false;
    this.currentUser = null;
    localStorage.removeItem('google_id_token');
  }

  isLoggedIn() { return this.hasToken(); }
  acceptTrial(token: string, termsAccepted: boolean) {
    return this.http.post(this.url('/api/auth/accept-trial'), { token, termsAccepted }, this.headers());
  }
  companyCount(): Observable<{ count: number; geoAddressCount: number; recentlyImportedCount: number }> {
    return this.http.get<{ count: number; geoAddressCount: number; recentlyImportedCount: number }>(
      this.url('/api/statistics/companies'), { params: new HttpParams().set('v', '2') }).pipe(tap(result => {
        if (!result || ![result.count, result.geoAddressCount, result.recentlyImportedCount]
            .every(value => typeof value === 'number' && Number.isFinite(value) && value >= 0)) {
          throw new Error('Incomplete home statistics response.');
        }
      }));
  }
  hasToken() {
    return !!this.token || (this.localSession && Number(localStorage.getItem('coldcall_session_expires')) > Date.now());
  }
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
  registrationConfig() {
    return this.http.get<{ siteKey: string; configured: boolean }>(this.url('/api/account/registration-config'));
  }
  registrationEmailExists(email: string) {
    return this.accountPost<{ exists: boolean }>('email-exists', { email });
  }
  registerLocal(details: ProfileDetails, email: string, password: string, turnstileToken: string) {
    return this.accountPost<{ message: string }>('register', { ...details, email, password, turnstileToken });
  }
  loginLocal(email: string, password: string) {
    this.purchasedPlan = null;
    this.expiredTrialNotified = false;
    this.expiredTrialNotice.next(false);
    this.csrfToken = ''; this.csrfRequest = null;
    this.authenticated = false; this.currentUser = null;
    this.token = ''; localStorage.removeItem('google_id_token');
    return this.accountPost<{ trialOffer?: TrialOffer; sessionExpiresAtUtc: string }>('login', { email, password }).pipe(
      tap(result => this.rememberSession(result.sessionExpiresAtUtc)),
      switchMap(result => this.refreshCsrf().pipe(map(() => result)))
    );
  }
  forgotPassword(email: string) { return this.accountPost<{ message: string }>('forgot-password', { email }); }
  resendConfirmation(email: string) { return this.accountPost<{ message: string }>('resend-confirmation', { email }); }
  confirmEmail(email: string, token: string) { return this.accountPost<{ message: string }>('confirm-email', { email, token }); }
  resetPassword(email: string, token: string, password: string) { return this.accountPost<{ message: string }>('reset-password', { email, token, password }); }
  logoutSession() { return this.accountPost<any>('logout', {}).pipe(tap(() => this.logout())); }

  authConfig(): Observable<AuthConfig> { return this.http.get<AuthConfig>(this.url('/auth-config.json')); }
  maintenanceStatus(): Observable<{ applicationInService: boolean }> {
    return this.http.get<{ applicationInService: boolean }>(this.url('/api/system/status'));
  }
  profile(): Observable<UserProfile> { return this.http.get<UserProfile>(this.url('/api/auth/profile'), this.headers()); }
  adminRegulamin(): Observable<{ globalSettingsId: number; regulamin: string | null }> {
    return this.http.get<{ globalSettingsId: number; regulamin: string | null }>(this.url('/api/admin/regulamin'), this.headers());
  }
  adminLogins(page: number, sortBy: string, direction: string): Observable<{ items: AdminUserLogin[]; total: number; page: number; pageSize: number }> {
    return this.http.get<{ items: AdminUserLogin[]; total: number; page: number; pageSize: number }>(this.url('/api/admin/logins'),
      { ...this.headers(), params: { page: String(page), sortBy, direction } });
  }
  saveAdminRegulamin(globalSettingsId: number, regulamin: string): Observable<void> {
    return this.http.put<void>(this.url('/api/admin/regulamin/' + globalSettingsId), { globalSettingsId, regulamin }, this.headers());
  }
  updateProfile(details: ProfileDetails) { return this.http.put<void>(this.url('/api/auth/profile'), details, this.headers()); }
  purchases(sortBy = 'paidAtUtc', direction = 'desc'): Observable<StripePurchase[]> {
    return this.http.get<StripePurchase[]>(this.url('/api/auth/purchases'), { ...this.headers(), params: { sortBy, direction } });
  }
  me(notifyExpiredTrial = true): Observable<Me> {
    const csrf = this.localSession && !this.csrfToken ? this.refreshCsrf() : of(this.csrfToken);
    return csrf.pipe(switchMap(() => this.http.get<Me>(this.url('/api/auth/me'), this.headers())),
      switchMap(user => user.isAdmin || user.termsOfServiceAccepted === true ? of(user)
        : this.termsConsent.requireConsent().pipe(switchMap(() => this.me(notifyExpiredTrial)))),
      tap(user => {
        if (!this.authenticated) {
          this.adminMaintenanceNotice = user.isAdmin && !!user.applicationInService;
        }
        this.currentUser = user;
        this.authenticated = true;
        if (notifyExpiredTrial && !user.isAdmin && !user.applicationInService && !user.hasActivePlan && (user.hasExpiredPlan || user.hasExpiredTrial) && !this.expiredTrialNotified) {
          this.expiredTrialNotified = true;
          this.expiredTrialNotice.next(true);
        }
      })
    );
  }
  pkd(term: string = ''): Observable<Pkd[]> { return this.http.get<Pkd[]>(this.url('/api/pkd'), { ...this.headers(), params: { term } }); }
  termsOfService(): Observable<{ regulamin: string | null }> {
    return this.http.get<{ regulamin: string | null }>(this.url('/api/auth/terms'), this.headers());
  }
  decideTermsOfService(accepted: boolean): Observable<{ accepted: boolean }> {
    return this.http.post<{ accepted: boolean }>(this.url('/api/auth/terms'), { accepted }, this.headers());
  }
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
  deleteSearch(id: number): Observable<void> {
    return this.http.delete<void>(this.url('/api/searches/' + id), this.headers());
  }
  distanceLocalities(term: string): Observable<AddressOption[]> {
    return this.http.get<AddressOption[]>(this.url('/api/address/distance-localities'), { ...this.headers(), params: { term } });
  }
  saveSearch(name: string, pkdCodeIds: number[], criteria?: SearchCriteria, profileId?: number, confirmDuplicateCriteria = false): Observable<SearchProfile> {
    if (profileId != null) {
      return this.http.put<SearchProfile>(this.url('/api/searches/' + profileId), { name, pkdCodeIds, criteria }, this.headers());
    }
    return this.http.post<SearchProfile>(this.url('/api/searches'), { name, pkdCodeIds, criteria, confirmDuplicateCriteria }, this.headers());
  }

  companies(page: number, sortBy: string, direction: string, pkdIds: number[], filters?: CompanyFilters, modifiedWithinDays?: number, notesOnly = false): Observable<PagedResult<CompanyShort>> {
    const params = this.companyParams(page, sortBy, direction, pkdIds, filters, modifiedWithinDays);
    return this.http.get<PagedResult<CompanyShort>>(this.url('/api/companies'), { ...this.headers(), params: notesOnly ? params.set('notesOnly', 'true') : params });
  }

  private companyParams(page: number, sortBy: string, direction: string, pkdIds: number[], filters?: CompanyFilters, modifiedWithinDays?: number): HttpParams {
    let params = new HttpParams().set('page', String(page)).set('pageSize', '10').set('sortBy', sortBy).set('direction', direction);
    if (modifiedWithinDays != null) { params = params.set('modifiedWithinDays', String(modifiedWithinDays)); }
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    if (filters) {
      params = params.set('companyStatusIds', JSON.stringify(filters.companyStatusIds || [1]));
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
  companyStatuses(): Observable<AddressOption[]> {
    return this.http.get<AddressOption[]>(this.url('/api/company-statuses'), this.headers()).pipe(
      map(statuses => statuses.map(status => status.name === 'Zawieszony'
        ? { ...status, name: 'Zawieszona' } : status))
    );
  }
  companyNotes(id: number): Observable<{ notes: string }> {
    return this.http.get<{ notes: string }>(this.url('/api/companies/' + id + '/notes'), this.headers());
  }
  saveCompanyNotes(id: number, notes: string): Observable<{ notes: string }> {
    return this.http.put<{ notes: string }>(this.url('/api/companies/' + id + '/notes'), { notes }, this.headers());
  }

  exportCompanies(searchProfileId: number, modifiedWithinDays?: number, sortBy = 'dataAktywnosci', direction = 'desc', searchName?: string, notesOnly = false, resultTab?: string) {
    return this.http.post(this.url('/api/companies/export'), { searchProfileId, modifiedWithinDays, sortBy, direction, searchName, notesOnly, resultTab },
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

  csvExportQuota(searchProfileId: number): Observable<{ used: number; limit: number | null; remaining: number | null; exportBlockedUntilUtc?: string | null }> {
    return this.http.get<{ used: number; limit: number | null; remaining: number | null; exportBlockedUntilUtc?: string | null }>(
      this.url('/api/companies/export-quota'), { ...this.headers(), params: { searchProfileId: String(searchProfileId) } });
  }

  deleteCsvExport(id: number): Observable<void> {
    return this.http.delete<void>(this.url('/api/companies/exports/' + id), this.headers());
  }

  csvDownloadUrl(row: CsvExportRow) {
    return this.url('/api/companies/DownloadCSV') + '?searchProfileId=' + row.searchProfileId
      + '&fileName=' + encodeURIComponent(row.csvFileName);
  }

  downloadCsv(row: CsvExportRow) {
    return this.http.get(this.csvDownloadUrl(row), { ...this.headers(), responseType: 'blob' });
  }

  plans(): Observable<Plan[]> { return this.http.get<Plan[]>(this.url('/api/plans'), this.headers()); }
  checkout(planCode: string) {
    return this.http.post<{ url: string }>(this.url('/api/plans/checkout'), { planCode }, this.headers());
  }

  confirmCheckout(sessionId: string) {
    const csrf = this.localSession && !this.csrfToken ? this.refreshCsrf() : of(this.csrfToken);
    return csrf.pipe(switchMap(() => this.http.post<{ paid: boolean }>(this.url('/api/plans/confirm-checkout'), { sessionId }, this.headers())));
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
  adminUserPayments(id: number): Observable<AdminUserPayment[]> {
    return this.http.get<AdminUserPayment[]>(this.url('/api/admin/users/' + id + '/payments'), this.headers());
  }
  updateUserAdmin(id: number, isAdmin: boolean): Observable<{ isAdmin: boolean }> {
    return this.http.put<{ isAdmin: boolean }>(this.url('/api/admin/users/' + id + '/admin'), { isAdmin }, this.headers());
  }
  createAdminUser(request: { email: string; password: string; disablePlanPurchase: boolean; planDefinitionId: number }): Observable<{ id: number; email: string }> {
    return this.http.post<{ id: number; email: string }>(this.url('/api/admin/users'), request, this.headers());
  }
  extendUserPlan(id: number) { return this.http.post(this.url('/api/admin/users/' + id + '/extend-plan'), {}, this.headers()); }
  adminImports(): Observable<AdminImport[]> {
    return this.http.get<AdminImport[]>(this.url('/api/admin/imports'), this.headers());
  }
  adminGlobalSettings(): Observable<AdminGlobalSetting[]> {
    return this.http.get<AdminGlobalSetting[]>(this.url('/api/admin/global-settings'), this.headers());
  }
  saveAdminGlobalSettings(settings: AdminGlobalSetting): Observable<void> {
    return this.http.put<void>(this.url('/api/admin/global-settings/' + settings.globalSettingsId), settings, this.headers())
      .pipe(tap(() => { if (this.currentUser) { this.currentUser.applicationInService = settings.applicationInService; } }));
  }
  deleteAdminImport(id: number): Observable<void> {
    return this.http.delete<void>(this.url('/api/admin/imports/' + id), this.headers());
  }
  adminImportSummary(id: number): Observable<{ summary: string | null }> {
    return this.http.get<{ summary: string | null }>(this.url('/api/admin/imports/' + id + '/summary'), this.headers());
  }
  adminPlans(upgrade: boolean): Observable<AdminPlanDefinition[]> {
    return this.http.get<AdminPlanDefinition[]>(this.url('/api/admin/plans'), { ...this.headers(), params: { upgrade: String(upgrade) } });
  }
  saveAdminPlan(plan: AdminPlanDefinition): Observable<void> {
    return this.http.put<void>(this.url('/api/admin/plans/' + plan.id), plan, this.headers());
  }
  updateUserEnabled(id: number, isEnabled: boolean): Observable<{ isEnabled: boolean }> {
    return this.http.put<{ isEnabled: boolean }>(this.url('/api/admin/users/' + id + '/enabled'),
      { isEnabled }, this.headers());
  }
  updateUserPlanPurchase(id: number, disablePlanPurchase: boolean): Observable<{ disablePlanPurchase: boolean }> {
    return this.http.put<{ disablePlanPurchase: boolean }>(this.url('/api/admin/users/' + id + '/plan-purchase'),
      { disablePlanPurchase }, this.headers());
  }
  adminPlanDefinitions(): Observable<PlanDefinitionOption[]> {
    return this.http.get<PlanDefinitionOption[]>(this.url('/api/admin/plan-definitions'), this.headers());
  }
  changeUserPlan(id: number, planDefinitionId: number): Observable<void> {
    return this.http.put<void>(this.url('/api/admin/users/' + id + '/plan'), { planDefinitionId }, this.headers());
  }
  deleteUserCsvExports(id: number): Observable<{ deleted: number }> {
    return this.http.delete<{ deleted: number }>(this.url('/api/admin/users/' + id + '/csv-exports'), this.headers());
  }
  deleteUserAccount(id: number): Observable<void> {
    return this.http.delete<void>(this.url('/api/admin/users/' + id), this.headers());
  }
  csvDeletionQuota(): Observable<{ limit: number | null; used: number; remaining: number | null }> {
    return this.http.get<{ limit: number | null; used: number; remaining: number | null }>(this.url('/api/companies/exports/deletion-quota'), this.headers());
  }
  grantPlan(userId: number, planCode: string) { return this.http.post(this.url('/api/admin/users/grant-plan'), { userId, planCode }, this.headers()); }
}
