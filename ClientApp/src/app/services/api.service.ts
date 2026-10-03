import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';

export interface SortableColumn { key: string; label: string; }
export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; sortableColumns?: SortableColumn[]; }
export interface Pkd { id: number; code: string; name: string; }
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
  latitude: number; longitude: number; prngId: string | null;
}
export interface BusinessType { typDzialalnosciId: number; typDzialalnosci: string; }
export interface CompanyDetails { company: Company; pkdCodes: Pkd[]; addresses: CompanyAddressDetails[]; }
export interface CompanyFilters {
  companyName?: string;
  address: string; hasPhone: boolean | null; hasEmail: boolean | null; hasWebsite: boolean | null;
  startedFrom?: string; startedTo?: string; recordLimit: number;
  wojewodztwoId?: number; powiatId?: number; gminaId?: number; miejscowoscId?: number; radiusKm?: number;
}
export interface CompanyShort {
  id: number; name: string; nip?: string; fullAddress: string;
  phone?: string; email?: string; www?: string; dataAktywnosci?: string;
  typyDzialalnosci?: string;
}
export interface SearchCriteria {
  companyName?: string;
  sortColumn: string; sortDirection: string; address: string;
  hasPhone: boolean | null; hasEmail: boolean | null; hasWebsite: boolean | null;
  dateRangeEnabled: boolean; dateFrom: string | null; dateTo: string | null; recordLimit: number;
}
export interface SearchProfile { id: number; name: string; pkdCodes: Pkd[]; criteria?: SearchCriteria; }
export interface Plan { code: string; searchLimit: number; descriptionHtml: string; price: number; currency: string; activeUntil?: string; }
export interface Me { id: number; googleEmail: string; displayName?: string; pictureUrl?: string; isAdmin: boolean; hasActivePlan: boolean; plan?: any; }
export interface UserPlan { planCode: string; purchasedAtUtc: string; expiresAtUtc: string; source: string; }
export interface AppUser { id: number; googleEmail: string; displayName?: string; plans: UserPlan[]; }
export interface AuthConfig { googleClientId: string; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  private token = localStorage.getItem('google_id_token') || '';
  private authenticated = false;
  private currentUser: Me | null = null;

  constructor(private http: HttpClient) {}

  setToken(token: string) {
    this.token = token;
    this.authenticated = false;
    localStorage.setItem('google_id_token', token);
  }

  logout() {
    this.token = '';
    this.authenticated = false;
    this.currentUser = null;
    localStorage.removeItem('google_id_token');
  }

  isLoggedIn() { return !!this.token; }
  hasToken() { return !!this.token; }
  isAuthenticated() { return this.authenticated; }
  isAdmin() { return this.authenticated && !!this.currentUser && this.currentUser.isAdmin; }
  isNonAdmin() { return this.authenticated && !!this.currentUser && !this.currentUser.isAdmin; }
  user() { return this.currentUser; }

  headers() {
    return { headers: new HttpHeaders({ Authorization: 'Bearer ' + this.token }) };
  }

  private url(path: string) { return path; }

  authConfig(): Observable<AuthConfig> { return this.http.get<AuthConfig>(this.url('/auth-config.json')); }
  me(): Observable<Me> {
    return this.http.get<Me>(this.url('/api/auth/me'), this.headers()).pipe(
      tap(user => {
        this.currentUser = user;
        this.authenticated = true;
      })
    );
  }
  pkd(term: string = ''): Observable<Pkd[]> { return this.http.get<Pkd[]>(this.url('/api/pkd'), { ...this.headers(), params: { term } }); }
  addressOptions(level: AddressLevel, parentIds: number[] = []): Observable<AddressOption[]> {
    let params = new HttpParams();
    ['wojewodztwoId', 'powiatId', 'gminaId'].forEach((key, index) => {
      if (parentIds[index]) { params = params.set(key, String(parentIds[index])); }
    });
    return this.http.get<AddressOption[]>(this.url('/api/address/' + level), { ...this.headers(), params });
  }
  searches(): Observable<SearchProfile[]> { return this.http.get<SearchProfile[]>(this.url('/api/searches'), this.headers()); }
  saveSearch(name: string, pkdCodeIds: number[], criteria?: SearchCriteria): Observable<SearchProfile> {
    return this.http.post<SearchProfile>(this.url('/api/searches'), { name, pkdCodeIds, criteria }, this.headers());
  }

  companies(page: number, sortBy: string, direction: string, pkdIds: number[], filters?: CompanyFilters): Observable<PagedResult<CompanyShort>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', '10').set('sortBy', sortBy).set('direction', direction);
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    if (filters) {
      if (filters.companyName && filters.companyName.trim()) { params = params.set('companyName', filters.companyName.trim()); }
      if (filters.address.trim()) { params = params.set('address', filters.address.trim()); }
      ['wojewodztwoId', 'powiatId', 'gminaId', 'miejscowoscId', 'radiusKm'].forEach(key => {
        if (filters[key] !== undefined && filters[key] !== null) { params = params.set(key, String(filters[key])); }
      });
      ['hasPhone', 'hasEmail', 'hasWebsite'].forEach(key => {
        if (filters[key] !== null) { params = params.set(key, String(filters[key])); }
      });
      if (filters.startedFrom && filters.startedTo) {
        params = params.set('startedFrom', filters.startedFrom).set('startedTo', filters.startedTo);
      }
      params = params.set('recordLimit', String(filters.recordLimit));
    }
    return this.http.get<PagedResult<CompanyShort>>(this.url('/api/companies'), { ...this.headers(), params });
  }

  companyDetails(id: number): Observable<CompanyDetails> {
    return this.http.get<CompanyDetails>(this.url('/api/companies/' + id), this.headers());
  }

  exportCompanies(pkdIds: number[]) {
    let params = new HttpParams();
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    return this.http.get(this.url('/api/companies/export'), { ...this.headers(), params, responseType: 'blob' });
  }

  plans(): Observable<Plan[]> { return this.http.get<Plan[]>(this.url('/api/plans'), this.headers()); }
  checkout(planCode: string) {
    return this.http.post<any>(this.url('/api/plans/checkout'), {
      planCode,
      successUrl: location.origin + '/plans?success=true',
      cancelUrl: location.origin + '/plans?canceled=true'
    }, this.headers());
  }

  adminCompanies(page: number, sortBy: string, direction: string, filter: string, pkdIds: number[] = []): Observable<PagedResult<Company>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', '10').set('sortBy', sortBy).set('direction', direction).set('filter', filter);
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    return this.http.get<PagedResult<Company>>(this.url('/api/admin/companies'), { ...this.headers(), params });
  }
  updateCompany(company: Company) { return this.http.put(this.url('/api/admin/companies/' + company.id), company, this.headers()); }
  businessTypes(): Observable<BusinessType[]> { return this.http.get<BusinessType[]>(this.url('/api/admin/business-types'), this.headers()); }
  users(): Observable<AppUser[]> { return this.http.get<AppUser[]>(this.url('/api/admin/users'), this.headers()); }
  grantPlan(userId: number, planCode: string) { return this.http.post(this.url('/api/admin/users/grant-plan'), { userId, planCode }, this.headers()); }
}
