import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number; }
export interface Pkd { id: number; code: string; name: string; }
export interface Company {
  id: number; name: string; businessType: string; firstName?: string; lastName?: string; krs?: string; nip?: string;
  wojewodztwo?: string; city?: string; gmina?: string; powiat?: string; postalCode?: string; street?: string;
  buildingNumber?: string; apartmentNumber?: string; phone?: string; email?: string; googleEmail?: string;
  website?: string; instagramAccount?: string; facebookAccount?: string; startedAt?: string;
}
export interface SearchProfile { id: number; name: string; pkdCodes: Pkd[]; }
export interface Plan { code: string; searchLimit: number; price: number; currency: string; activeUntil?: string; }
export interface Me { id: number; googleEmail: string; displayName?: string; isAdmin: boolean; hasActivePlan: boolean; plan?: any; }
export interface UserPlan { planCode: string; purchasedAtUtc: string; expiresAtUtc: string; source: string; }
export interface AppUser { id: number; googleEmail: string; displayName?: string; plans: UserPlan[]; }

@Injectable({ providedIn: 'root' })
export class ApiService {
  private token = localStorage.getItem('google_id_token') || '';

  constructor(private http: HttpClient) {}

  setToken(token: string) {
    this.token = token;
    localStorage.setItem('google_id_token', token);
  }

  logout() {
    this.token = '';
    localStorage.removeItem('google_id_token');
  }

  isLoggedIn() { return !!this.token; }

  headers() {
    return { headers: new HttpHeaders({ Authorization: 'Bearer ' + this.token }) };
  }

  me(): Observable<Me> { return this.http.get<Me>('/api/auth/me', this.headers()); }
  pkd(term: string): Observable<Pkd[]> { return this.http.get<Pkd[]>('/api/pkd', { ...this.headers(), params: { term } }); }
  searches(): Observable<SearchProfile[]> { return this.http.get<SearchProfile[]>('/api/searches', this.headers()); }
  saveSearch(name: string, pkdCodeIds: number[]): Observable<SearchProfile> {
    return this.http.post<SearchProfile>('/api/searches', { name, pkdCodeIds }, this.headers());
  }

  companies(page: number, sortBy: string, direction: string, pkdIds: number[]): Observable<PagedResult<Company>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', '20').set('sortBy', sortBy).set('direction', direction);
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    return this.http.get<PagedResult<Company>>('/api/companies', { ...this.headers(), params });
  }

  exportCompanies(pkdIds: number[]) {
    let params = new HttpParams();
    pkdIds.forEach(id => params = params.append('pkdIds', String(id)));
    return this.http.get('/api/companies/export', { ...this.headers(), params, responseType: 'blob' });
  }

  plans(): Observable<Plan[]> { return this.http.get<Plan[]>('/api/plans', this.headers()); }
  checkout(planCode: string) {
    return this.http.post<any>('/api/plans/checkout', {
      planCode,
      successUrl: location.origin + '/plans?success=true',
      cancelUrl: location.origin + '/plans?canceled=true'
    }, this.headers());
  }

  adminCompanies(page: number, sortBy: string, direction: string, filter: string): Observable<PagedResult<Company>> {
    const params = { page: String(page), pageSize: '20', sortBy, direction, filter };
    return this.http.get<PagedResult<Company>>('/api/admin/companies', { ...this.headers(), params });
  }
  updateCompany(company: Company) { return this.http.put('/api/admin/companies/' + company.id, company, this.headers()); }
  users(): Observable<AppUser[]> { return this.http.get<AppUser[]>('/api/admin/users', this.headers()); }
  grantPlan(userId: number, planCode: string) { return this.http.post('/api/admin/users/grant-plan', { userId, planCode }, this.headers()); }
}
