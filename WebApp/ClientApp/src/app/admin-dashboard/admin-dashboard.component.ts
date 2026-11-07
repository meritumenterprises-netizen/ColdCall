import { Component, ElementRef, Input, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { isFutureActivityDate } from '../services/activity-date';
import { PasswordGenerator } from '../services/password-generator';
import { Subscription, timer } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { AddressLevel, AddressOption, ApiService, AppUser, BusinessType, Company, CompanyFilters, Pkd, PlanDefinitionOption, SortableColumn } from '../services/api.service';

@Component({
  selector: 'app-admin-dashboard',
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.css']
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  isFutureActivityDate = isFutureActivityDate;
  updateCompanyNotes(event: { companyId: number; notes: string }) {
    const company = this.companies.find(row => row.id === event.companyId);
    if (company) { company.userNotes = event.notes; }
  }
  @ViewChild('setUserPasswordDialog', { static: false }) setUserPasswordDialog: ElementRef;
  busyUserIds = new Set<number>();
  passwordUser: { id: number; googleEmail: string } | null = null;
  userPassword = '';
  readonly userPasswordPattern = new RegExp('^(?=[\\s\\S]*\\p{L})(?=[\\s\\S]*[0-9])(?=[\\s\\S]*[^\\p{L}\\p{N}\\s])[\\s\\S]*$', 'u');
  settingUserPassword = false;
  userPasswordError = '';
  private userActionRequests = new Set<Subscription>();
  @ViewChild('createUserDialog', { static: false }) createUserDialog: ElementRef;
  newUserEmail = '';
  newUserPassword = '';
  newUserDisablePlanPurchase = false;
  newUserIsBeta = false;
  newUserSendCredentialsEmail = false;
  newUserPlanId: number | null = null;
  newUserPlans: PlanDefinitionOption[] = [];
  newUserPlansLoading = false;
  creatingUser = false;
  createUserError = '';
  private newUserPlansRequest: Subscription | null = null;

  get newUserPlanDescriptionHtml(): string {
    const plan = this.newUserPlans.find(item => item.id === this.newUserPlanId);
    return plan ? plan.descriptionHtml || '' : '';
  }

  requestCreateUser(isBetaUser = false) {
    if (this.creatingUser) { return; }
    this.newUserIsBeta = isBetaUser;
    if (this.newUserPlansRequest) { this.newUserPlansRequest.unsubscribe(); }
    this.newUserEmail = '';
    this.newUserPassword = '';
    this.newUserDisablePlanPurchase = false;
    this.newUserSendCredentialsEmail = false;
    this.newUserPlanId = null;
    this.newUserPlans = [];
    this.createUserError = '';
    this.newUserPlansLoading = true;
    this.createUserDialog.nativeElement.showModal();
    this.newUserPlansRequest = this.api.adminPlanDefinitions()
      .pipe(finalize(() => this.newUserPlansLoading = false)).subscribe(plans => {
        this.newUserPlans = plans;
        if (this.newUserIsBeta) {
          const defaultPlan = plans.find(plan => plan.code === 'PX3');
          this.newUserPlanId = defaultPlan ? defaultPlan.id : null;
        }
      },
        () => this.createUserError = 'Nie udalo sie pobrac planow.');
  }

  generateUserPassword() {
    if (this.creatingUser) { return; }
    try {
      this.newUserPassword = PasswordGenerator.generate();
      this.createUserError = '';
    } catch {
      this.createUserError = 'Nie udalo sie wygenerowac hasla. Wpisz haslo recznie.';
    }
  }

  cancelCreateUser() {
    if (this.creatingUser) { return; }
    if (this.newUserPlansRequest) { this.newUserPlansRequest.unsubscribe(); }
    this.newUserPassword = '';
    this.createUserDialog.nativeElement.close();
  }

  confirmCreateUser() {
    const email = this.newUserEmail.trim();
    if (this.creatingUser || this.newUserPlansLoading || !email || this.newUserPassword.length < 12
        || !this.newUserPlans.some(plan => plan.id === this.newUserPlanId)) { return; }
    this.creatingUser = true;
    this.createUserError = '';
    const sendCredentialsEmail = this.newUserSendCredentialsEmail;
    const request = this.api.createAdminUser({ email, password: this.newUserPassword,
      disablePlanPurchase: this.newUserDisablePlanPurchase, planDefinitionId: this.newUserPlanId,
      isBetaUser: this.newUserIsBeta, sendCredentialsEmail })
      .pipe(finalize(() => this.creatingUser = false)).subscribe(result => {
        this.newUserPassword = '';
        this.createUserDialog.nativeElement.close();
        this.message = 'Uzytkownik zostal utworzony.';
        if (sendCredentialsEmail) {
          this.message += result.credentialsEmailSent ? ' Dane logowania wyslano na email uzytkownika.'
            : ' Nie udalo sie wyslac danych logowania na email. Konto zostalo utworzone; nie tworz go ponownie.';
        }
        this.openAdminTab('users');
        this.loadUsers();
      }, err => {
        const errors = err.error && err.error.errors;
        this.createUserError = err.error && err.error.message ? err.error.message
          : errors ? Object.keys(errors).map(key => errors[key].join(' ')).join(' ')
          : 'Nie udalo sie utworzyc uzytkownika.';
      });
    this.userActionRequests.add(request);
  }
  updateUserEnabled(userId: number, enabled: boolean) {
    const user = this.users.find(item => item.id === userId);
    if (!user || user.isAdmin || this.busyUserIds.has(userId)) { return; }
    const previous = !!user.isEnabled;
    user.isEnabled = enabled;
    this.busyUserIds.add(userId);
    const request = this.api.updateUserEnabled(userId, enabled)
      .pipe(finalize(() => this.busyUserIds.delete(userId))).subscribe(result => {
        user.isEnabled = result.isEnabled;
      }, () => {
        user.isEnabled = previous;
        this.message = 'Nie udalo sie zapisac statusu uzytkownika.';
      });
    this.userActionRequests.add(request);
  }
  get currentUserId(): number | null {
    const user = this.api.user();
    return user ? user.id : null;
  }
  deleteUserAccount(userId: number) {
    const user = this.users.find(item => item.id === userId);
    if (!user || user.isAdmin || this.currentUserId === null || userId === this.currentUserId || this.busyUserIds.has(userId)) { return; }
    if (!window.confirm('Czy naprawd\u0119 chcesz usun\u0105\u0107 konto ' + user.googleEmail
      + '? Zostan\u0105 usuni\u0119te tak\u017ce plany, wyszukiwania, notatki, historia zakup\u00f3w, dane logowania i pliki CSV. Tej operacji nie mo\u017cna cofn\u0105\u0107.')) { return; }
    this.busyUserIds.add(userId);
    this.message = '';
    const request = this.api.deleteUserAccount(userId).pipe(finalize(() => this.busyUserIds.delete(userId))).subscribe(() => {
      this.users = this.users.filter(item => item.id !== userId);
      if (this.selectedUserId === userId) { this.selectedUserId = null; }
      this.usersPage = Math.min(this.usersPage, this.usersTotalPages);
      this.message = 'Konto zosta\u0142o usuni\u0119te.';
    }, err => this.message = err.error && err.error.message ? err.error.message : 'Nie uda\u0142o si\u0119 usun\u0105\u0107 konta.');
    this.userActionRequests.add(request);
  }
  updateUserBeta(userId: number, isBetaUser: boolean) {
    const user = this.users.find(item => item.id === userId);
    if (!user || !!user.isBetaUser === isBetaUser || this.busyUserIds.has(userId)) { return; }
    this.busyUserIds.add(userId);
    const request = this.api.updateUserBeta(userId, isBetaUser)
      .pipe(finalize(() => this.busyUserIds.delete(userId))).subscribe(result => {
        user.isBetaUser = result.isBetaUser;
        if (result.isBetaUser && this.usersTab === 'all') {
          this.selectUsersTab('beta');
          this.selectedUserId = userId;
          const position = this.userRows.findIndex(row => row.id === userId);
          this.changeUsersPage(position < 0 ? 1 : Math.floor(position / this.usersPageSize) + 1);
        } else {
          this.changeUsersPage(this.usersPage);
        }
      }, () => {
        this.message = 'Nie udalo sie zapisac statusu beta uzytkownika.';
      });
    this.userActionRequests.add(request);
  }
  @ViewChild('promoteUserAdminDialog', { static: false }) promoteUserAdminDialog: ElementRef;
  userToPromote: { id: number; googleEmail: string } | null = null;
  promotingUserAdmin = false;
  promoteUserAdminError = '';

  requestUserAdmin(userId: number, event: Event) {
    event.stopPropagation();
    const input = event.target as HTMLInputElement;
    const checked = input.checked;
    const user = this.users.find(item => item.id === userId);
    input.checked = !!user && !!user.isAdmin;
    if (!user || this.currentUserId === null || userId === this.currentUserId
        || this.busyUserIds.has(userId) || this.promotingUserAdmin) { return; }
    if (!checked) {
      if (user.isAdmin) { this.revokeUserAdmin(user); }
      return;
    }
    if (user.isAdmin) { return; }
    this.userToPromote = { id: user.id, googleEmail: user.googleEmail };
    this.promoteUserAdminError = '';
    this.promoteUserAdminDialog.nativeElement.showModal();
  }

  private revokeUserAdmin(user: AppUser) {
    this.busyUserIds.add(user.id);
    const request = this.api.updateUserAdmin(user.id, false)
      .pipe(finalize(() => this.busyUserIds.delete(user.id))).subscribe(result => {
        user.isAdmin = result.isAdmin;
        this.changeUsersPage(this.usersPage);
      }, () => this.message = 'Nie udalo sie odebrac uprawnien administratora. Sprobuj ponownie.');
    this.userActionRequests.add(request);
  }

  cancelUserAdmin() {
    if (this.promotingUserAdmin) { return; }
    this.promoteUserAdminDialog.nativeElement.close();
    this.userToPromote = null;
    this.promoteUserAdminError = '';
  }

  confirmUserAdmin() {
    if (!this.userToPromote || this.promotingUserAdmin) { return; }
    const user = this.users.find(item => item.id === this.userToPromote.id);
    if (!user || user.isAdmin || this.currentUserId === null || user.id === this.currentUserId
        || this.busyUserIds.has(user.id)) { return; }
    this.promotingUserAdmin = true;
    this.promoteUserAdminError = '';
    this.busyUserIds.add(user.id);
    const request = this.api.updateUserAdmin(user.id, true)
      .pipe(finalize(() => { this.busyUserIds.delete(user.id); this.promotingUserAdmin = false; }))
      .subscribe(result => {
        user.isAdmin = result.isAdmin;
        this.promoteUserAdminDialog.nativeElement.close();
        this.userToPromote = null;
      }, () => {
        this.promoteUserAdminError = 'Nie udalo sie zapisac uprawnien administratora. Sprobuj ponownie.';
      });
    this.userActionRequests.add(request);
  }
  updatePlanPurchase(userId: number, disabled: boolean) {
    const user = this.users.find(item => item.id === userId);
    if (!user || this.busyUserIds.has(userId)) { return; }
    const previous = !!user.disablePlanPurchase;
    user.disablePlanPurchase = disabled;
    this.busyUserIds.add(userId);
    const request = this.api.updateUserPlanPurchase(userId, disabled)
      .pipe(finalize(() => this.busyUserIds.delete(userId))).subscribe(result => {
        user.disablePlanPurchase = result.disablePlanPurchase;
      }, () => {
        user.disablePlanPurchase = previous;
        this.message = 'Nie udalo sie zapisac ustawienia zakupu planu.';
      });
    this.userActionRequests.add(request);
  }
  @ViewChild('changePlanDialog', { static: false }) changePlanDialog: ElementRef;
  userToChangePlan: { id: number; googleEmail: string; planCode: string } | null = null;
  planDefinitions: PlanDefinitionOption[] = [];
  selectedPlanDefinitionId: number | null = null;
  planDefinitionsLoading = false;
  changingPlan = false;
  changePlanError = '';
  private planDefinitionsRequest: Subscription | null = null;

  get selectedPlanDescriptionHtml(): string {
    const plan = this.planDefinitions.find(item => item.id === this.selectedPlanDefinitionId);
    return plan ? plan.descriptionHtml || '' : '';
  }

  requestChangePlan(row: { id: number; googleEmail: string; planCode: string }) {
    if (!row.planCode || this.busyUserIds.has(row.id) || this.changingPlan) { return; }
    if (this.planDefinitionsRequest) { this.planDefinitionsRequest.unsubscribe(); }
    this.userToChangePlan = row;
    this.planHint = null;
    this.planDefinitions = [];
    this.selectedPlanDefinitionId = null;
    this.changePlanError = '';
    this.planDefinitionsLoading = true;
    this.changePlanDialog.nativeElement.showModal();
    this.planDefinitionsRequest = this.api.adminPlanDefinitions(true)
      .pipe(finalize(() => this.planDefinitionsLoading = false)).subscribe(plans => {
        this.planDefinitions = plans;
        const current = plans.find(plan => plan.code.trim().toUpperCase() === row.planCode.trim().toUpperCase());
        this.selectedPlanDefinitionId = current ? current.id : null;
      }, () => this.changePlanError = 'Nie udalo sie pobrac planow.');
  }

  cancelChangePlan() {
    if (this.changingPlan) { return; }
    if (this.planDefinitionsRequest) { this.planDefinitionsRequest.unsubscribe(); }
    this.changePlanDialog.nativeElement.close();
    this.userToChangePlan = null;
    this.selectedPlanDefinitionId = null;
    this.changePlanError = '';
  }

  confirmChangePlan() {
    if (!this.userToChangePlan || this.changingPlan || this.planDefinitionsLoading
        || !this.planDefinitions.some(plan => plan.id === this.selectedPlanDefinitionId)) { return; }
    const userId = this.userToChangePlan.id;
    const planId = this.selectedPlanDefinitionId;
    this.changingPlan = true;
    this.busyUserIds.add(userId);
    this.changePlanError = '';
    const request = this.api.changeUserPlan(userId, planId)
      .pipe(finalize(() => { this.changingPlan = false; this.busyUserIds.delete(userId); }))
      .subscribe(() => {
        this.changePlanDialog.nativeElement.close();
        this.userToChangePlan = null;
        this.message = 'Plan zostal zmieniony.';
        this.loadUsers();
      }, err => this.changePlanError = err.error && err.error.message
        ? err.error.message : 'Nie udalo sie zmienic planu.');
    this.userActionRequests.add(request);
  }
  extendPlan(id: number) {
    if (this.busyUserIds.has(id)) { return; }
    this.busyUserIds.add(id);
    const request = this.api.extendUserPlan(id).pipe(finalize(() => this.busyUserIds.delete(id))).subscribe(() => {
      this.message = 'Plan przedluzono o 7 dni.';
      this.loadUsers();
    }, err => this.message = err.error && err.error.message ? err.error.message : 'Nie udalo sie przedluzyc planu.');
    this.userActionRequests.add(request);
  }
  canSetUserPassword(row: { id: number; googleEmail: string }) {
    return !this.busyUserIds.has(row.id) && !(this.usersTab === 'admins' && /@gmail\.com$/i.test(row.googleEmail.trim()));
  }
  requestSetUserPassword(row: { id: number; googleEmail: string }, event: Event) {
    event.stopPropagation();
    if (this.settingUserPassword || !this.canSetUserPassword(row)) { return; }
    this.passwordUser = row;
    this.userPassword = '';
    this.userPasswordError = '';
    this.setUserPasswordDialog.nativeElement.showModal();
  }
  cancelSetUserPassword() {
    if (this.settingUserPassword) { return; }
    this.setUserPasswordDialog.nativeElement.close();
    this.passwordUser = null;
    this.userPassword = '';
    this.userPasswordError = '';
  }
  generateSetUserPassword() {
    if (this.settingUserPassword) { return; }
    try {
      this.userPassword = PasswordGenerator.generate(16);
      this.userPasswordError = '';
    } catch {
      this.userPasswordError = 'Nie uda\u0142o si\u0119 wygenerowa\u0107 has\u0142a. Wpisz has\u0142o r\u0119cznie.';
    }
  }
  confirmSetUserPassword() {
    if (!this.passwordUser || this.settingUserPassword || this.userPassword.length < 12
        || this.userPassword.length > 128 || !this.userPasswordPattern.test(this.userPassword)) { return; }
    const userId = this.passwordUser.id;
    this.settingUserPassword = true;
    this.busyUserIds.add(userId);
    this.userPasswordError = '';
    const request = this.api.setAdminUserPassword(userId, this.userPassword)
      .pipe(finalize(() => { this.settingUserPassword = false; this.busyUserIds.delete(userId); }))
      .subscribe(result => {
        this.userPassword = '';
        this.passwordUser = null;
        this.setUserPasswordDialog.nativeElement.close();
        this.message = result.message;
      }, err => {
        const errors = err.error && err.error.errors;
        this.userPasswordError = err.error && err.error.message ? err.error.message
          : errors ? Object.keys(errors).map(key => errors[key].join(' ')).join(' ')
          : 'Nie udalo sie ustawic hasla uzytkownika.';
      });
    this.userActionRequests.add(request);
  }
  activeFilterTab = 'criteria';
  nipTerm = '';
  blockedByRodoFilter: boolean | null = null;
  cycleRodoFilter() {
    this.blockedByRodoFilter = this.blockedByRodoFilter === null ? true : this.blockedByRodoFilter === true ? false : null;
    this.updateFilters();
  }
  filters: CompanyFilters = { companyName: '', address: '', hasPhone: null, hasEmail: null, hasWebsite: null, recordLimit: 1000 };
  dateRangeEnabled = false;
  companyStatuses: AddressOption[] = [];
  companyStatusesError = '';
  private companyStatusesRequest: Subscription | null = null;

  loadCompanyStatuses() {
    if (this.companyStatusesRequest) { this.companyStatusesRequest.unsubscribe(); }
    this.companyStatusesError = '';
    this.companyStatusesRequest = this.api.companyStatuses().subscribe(statuses => this.companyStatuses = statuses,
      () => this.companyStatusesError = 'Nie udalo sie pobrac statusow dzialalnosci.');
  }

  toggleCompanyStatus(id: number, checked: boolean) {
    const selected = this.filters.companyStatusIds || [1];
    this.filters.companyStatusIds = (checked ? selected.concat(id) : selected.filter(value => value !== id))
      .filter((value, index, values) => values.indexOf(value) === index).sort((a, b) => a - b);
    this.updateFilters();
  }
  dateFrom = '2020-01-01';
  dateTo = new Date().toLocaleDateString('sv-SE');
  radiusEnabled = false;
  radiusKm = 1;
  radiusOptions = [1, 5, 10, 20, 50, 100, 200, 500];
  pkdDescription = '';
  classifyingPkd = false;
  filterError = '';
  addressFields: { level: AddressLevel; label: string; selectedId: number | null; term: string; options: AddressOption[]; loading: boolean; error: string; request: Subscription | null }[] = [
    { level: 'wojewodztwa', label: 'Województwo', selectedId: null, term: '', options: [], loading: false, error: '', request: null },
    { level: 'powiaty', label: 'Powiat', selectedId: null, term: '', options: [], loading: false, error: '', request: null },
    { level: 'gminy', label: 'Gmina', selectedId: null, term: '', options: [], loading: false, error: '', request: null },
    { level: 'miejscowosci', label: 'Miejscowość', selectedId: null, term: '', options: [], loading: false, error: '', request: null }
  ];
  private companiesRequest: Subscription | null = null;
  private pendingCompaniesLoad: { refresh: boolean; startedAt: number } | null = null;
  private companiesDestroyed = false;
  searchStartedAt = 0;
  searchFinishedAt = 0;
  companySearchError = '';
  private filterDelay: Subscription | null = null;
  private classificationRequest: Subscription | null = null;

  updateCompanyName() {
    if (this.filterDelay) { this.filterDelay.unsubscribe(); }
    const name = (this.filters.companyName || '').trim();
    if (name.length > 0 && name.length < 3) { return; }
    this.filterDelay = timer(3000).subscribe(() => this.updateFilters());
  }

  updateFilters(delay = false) {
    if (this.filterDelay) { this.filterDelay.unsubscribe(); }
    if (delay) { this.filterDelay = timer(300).subscribe(() => this.updateFilters()); return; }
    this.filterError = '';
    const normalizedNip = this.nipTerm.replace(/[-\s]/g, '');
    if (!/^\d{0,10}$/.test(normalizedNip)) { this.filterError = 'NIP moze zawierac maksymalnie 10 cyfr.'; return; }
    if (this.dateRangeEnabled && (!/^\d{4}-\d{2}-\d{2}$/.test(this.dateFrom) || !/^\d{4}-\d{2}-\d{2}$/.test(this.dateTo) || this.dateTo < this.dateFrom)) {
      this.filterError = 'Podaj poprawne daty. Data do nie moze byc wczesniejsza niz data od.'; return;
    }
    this.filters.startedFrom = this.dateRangeEnabled ? this.dateFrom : undefined;
    this.filters.startedTo = this.dateRangeEnabled ? this.dateTo : undefined;
    ['wojewodztwoId', 'powiatId', 'gminaId', 'miejscowoscId'].forEach((key, i) => this.filters[key] = this.addressFields[i].selectedId || undefined);
    this.filters.radiusKm = this.radiusEnabled ? this.radiusKm : undefined;
    this.page = 1;
    this.loadCompanies();
  }
  cyclePresence(key: string) {
    this.filters[key] = this.filters[key] === null ? true : this.filters[key] === true ? false : null;
    this.updateFilters();
  }
  presenceLabel(value: boolean | null) { return value === null ? 'Dowolne' : value ? 'Jest' : 'Brak'; }
  visibleAddressOptions(index: number) {
    const field = this.addressFields[index];
    return field.options.filter(option => option.name.toLowerCase().includes(field.term.toLowerCase()));
  }
  loadAddressOptions(index: number) {
    const field = this.addressFields[index];
    if (field.request) { field.request.unsubscribe(); }
    field.loading = true; field.error = '';
    field.request = this.api.addressOptions(field.level, this.addressFields.slice(0, index).map(f => f.selectedId))
      .pipe(finalize(() => field.loading = false)).subscribe(options => field.options = options.sort((a, b) => a.name.localeCompare(b.name, 'pl')),
        () => field.error = 'Nie udalo sie pobrac lokalizacji.');
  }
  selectAddress(index: number, id: number | null) {
    this.addressFields[index].selectedId = id;
    this.radiusEnabled = false;
    this.addressFields.slice(index + 1).forEach(field => {
      if (field.request) { field.request.unsubscribe(); }
      field.selectedId = null; field.options = []; field.term = ''; field.error = ''; field.loading = false;
    });
    if (id && index < 3) { this.loadAddressOptions(index + 1); }
    this.updateFilters();
  }
  classifyPkd() {
    if (!this.pkdDescription.trim() || this.classifyingPkd) { return; }
    this.classifyingPkd = true;
    this.classificationRequest = this.api.classifyPkd(this.pkdDescription).pipe(finalize(() => this.classifyingPkd = false)).subscribe(result => {
      this.selectedPkds = this.sortPkds(result.pkdCodes.slice(0, 10));
      this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
      this.updatePkdSuggestions(); this.updateFilters();
    }, () => this.filterError = 'Nie udalo sie rozpoznac kodow PKD.');
  }
  ngOnDestroy() {
    if (this.userDetailsRequest) { this.userDetailsRequest.unsubscribe(); }
    this.companiesDestroyed = true;
    this.pendingCompaniesLoad = null;
    this.userPassword = '';
    this.newUserPassword = '';
    if (this.newUserPlansRequest) { this.newUserPlansRequest.unsubscribe(); }
    if (this.planDefinitionsRequest) { this.planDefinitionsRequest.unsubscribe(); }
    if (this.companyStatusesRequest) { this.companyStatusesRequest.unsubscribe(); }
    this.userActionRequests.forEach(request => request.unsubscribe());
    this.rodoRequests.forEach(request => request.unsubscribe());
    if (this.companiesRequest) { this.companiesRequest.unsubscribe(); }
    if (this.filterDelay) { this.filterDelay.unsubscribe(); }
    if (this.classificationRequest) { this.classificationRequest.unsubscribe(); }
    this.addressFields.forEach(field => { if (field.request) { field.request.unsubscribe(); } });
  }
  tab: 'companies' | 'users' | 'payments' | 'plans' | 'imports' | 'global-settings' | 'regulamin' | 'regulamin-beta' | 'logins' = 'companies';
  companies: Company[] = [];
  users: AppUser[] = [];
  usersTab: 'all' | 'beta' | 'admins' = 'all';
  readonly usersTabs: { key: 'all' | 'beta' | 'admins'; label: string }[] = [
    { key: 'all', label: 'Wszyscy' },
    { key: 'beta', label: 'Beta użytkownicy' },
    { key: 'admins', label: 'Administratorzy' }
  ];
  selectUsersTab(tab: 'all' | 'beta' | 'admins') {
    this.usersTab = tab;
    this.usersPage = 1;
    this.selectedUserId = null;
    this.planHint = null;
  }
  navigateUsersTabs(event: KeyboardEvent, tab: 'all' | 'beta' | 'admins') {
    const keys = this.usersTabs.map(item => item.key);
    const position = keys.indexOf(tab);
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? keys.length - 1
      : event.key === 'ArrowRight' ? (position + 1) % keys.length
      : event.key === 'ArrowLeft' ? (position + keys.length - 1) % keys.length : -1;
    if (next < 0) { return; }
    event.preventDefault();
    this.selectUsersTab(keys[next]);
    const buttons = (event.currentTarget as HTMLElement).parentElement.querySelectorAll<HTMLButtonElement>('[role="tab"]');
    buttons[next].focus();
  }

  selectedUserId: number | null = null;
  private userDetailsDraft: (AppUser & { expiresInDays: number | null }) | null = null;
  savingUserDetails = false;
  userDetailsError = '';
  userDetailsMessage = '';
  private userDetailsRequest: Subscription | null = null;

  private createUserDetailsDraft(user: AppUser): AppUser & { expiresInDays: number | null } {
    const plan = (user.plans || []).slice().sort((a, b) => Date.parse(this.dateUtc(b.expiresAtUtc)) - Date.parse(this.dateUtc(a.expiresAtUtc)))[0];
    const expiration = plan ? Date.parse(this.dateUtc(plan.expiresAtUtc)) : NaN;
    const expiresInDays = !user.isAdmin && expiration > Date.now() ? Math.ceil((expiration - Date.now()) / 86400000) : null;
    return { ...user, expiresInDays };
  }

  get editableUserDetails(): (AppUser & { expiresInDays: number | null }) | null {
    const user = this.selectedUser;
    if (!user) { this.userDetailsDraft = null; return null; }
    if (!this.userDetailsDraft || this.userDetailsDraft.id !== user.id) {
      this.userDetailsDraft = this.createUserDetailsDraft(user);
      this.userDetailsError = ''; this.userDetailsMessage = '';
    }
    return this.userDetailsDraft;
  }

  cancelUserDetails() {
    if (this.savingUserDetails) { return; }
    this.userDetailsDraft = this.selectedUser ? this.createUserDetailsDraft(this.selectedUser) : null;
    this.userDetailsError = ''; this.userDetailsMessage = '';
  }

  saveUserDetails() {
    const draft = this.editableUserDetails;
    const user = this.selectedUser;
    if (!draft || !user || this.savingUserDetails) { return; }
    if (draft.expiresInDays != null && (!Number.isInteger(draft.expiresInDays) || draft.expiresInDays < 0 || draft.expiresInDays > 36500)) {
      this.userDetailsError = 'Podaj liczbe calkowita dni od 0 do 36500.';
      return;
    }
    const details = { firstName: draft.firstName, lastName: draft.lastName, companyName: draft.companyName,
      nip: draft.nip, phone: draft.phone, postalCode: draft.postalCode, city: draft.city,
      street: draft.street, streetNumber: draft.streetNumber, expiresInDays: draft.expiresInDays };
    this.savingUserDetails = true;
    this.userDetailsError = ''; this.userDetailsMessage = '';
    this.userDetailsRequest = this.api.updateAdminUserDetails(user.id, details)
      .pipe(finalize(() => this.savingUserDetails = false)).subscribe(saved => {
        const { expiresAtUtc, ...savedDetails } = saved;
        Object.assign(user, savedDetails);
        if (expiresAtUtc) {
          const plan = (user.plans || []).slice().sort((a, b) => Date.parse(this.dateUtc(b.expiresAtUtc)) - Date.parse(this.dateUtc(a.expiresAtUtc)))[0];
          if (plan) { plan.expiresAtUtc = expiresAtUtc; }
        }
        if (this.selectedUserId === user.id) {
          this.userDetailsDraft = this.createUserDetailsDraft(user);
          this.userDetailsMessage = 'Dane uzytkownika zostaly zapisane.';
        }
        this.loadUsers(user.id);
      }, err => {
        if (this.selectedUserId !== user.id) { return; }
        const body = err.error;
        const validation = body && body.errors && typeof body.errors === 'object'
          ? Object.keys(body.errors).map(key => key + ': ' + [].concat(body.errors[key]).join(' ')).join(' ') : '';
        this.userDetailsError = body && typeof body.message === 'string' ? body.message
          : validation ? validation
          : err.status === 404 ? 'Nie znaleziono uzytkownika lub endpointu zapisu. Odswiez liste; jesli blad pozostaje, przebuduj i uruchom ponownie backend.'
          : err.status === 401 ? 'Sesja wygasla. Zaloguj sie ponownie.'
          : err.status === 403 ? 'Brak uprawnien administratora do zapisu danych.'
          : err.status === 0 ? 'Brak polaczenia z serwerem.'
          : 'Nie udalo sie zapisac danych uzytkownika (HTTP ' + err.status + ').';
      });
  }
  get selectedUser(): AppUser | null {
    return this.pagedUserRows.some(row => row.id === this.selectedUserId)
      ? this.users.find(user => user.id === this.selectedUserId) || null : null;
  }
  selectUser(id: number, event?: KeyboardEvent) {
    if (event && event.target !== event.currentTarget) { return; }
    if (event) { event.preventDefault(); }
    this.selectedUserId = id;
    this.planHint = null;
  }
  usersLoading = false;
  usersPage = 1;
  readonly usersPageSize = 10;
  get usersTotalPages() { return Math.max(1, Math.ceil(this.userRows.length / this.usersPageSize)); }
  get pagedUserRows() {
    const start = (this.usersPage - 1) * this.usersPageSize;
    return this.userRows.slice(start, start + this.usersPageSize);
  }
  changeUsersPage(page: number) {
    this.usersPage = Math.max(1, Math.min(page, this.usersTotalPages));
    this.planHint = null;
  }
  usersSortBy = 'displayName';
  usersDirection = 'asc';
  userColumns: SortableColumn[] = [
    { key: 'displayName', label: 'Imi\u0119 i nazwisko' }, { key: 'googleEmail', label: 'Email' },
    { key: 'planCode', label: 'Aktualny plan' }, { key: 'createdAtUtc', label: 'Login utworzony' },
    { key: 'expiresAtUtc', label: 'Data wyga\u015bni\u0119cia' },
    { key: 'disablePlanPurchase', label: 'Nie pozw\u00f3l na zakup planu' },
    { key: 'isEnabled', label: 'Aktywny' },
    { key: 'isAdmin', label: 'Admin' },
    { key: 'isBetaUser', label: 'Beta user' }
  ];
  get visibleUserColumns() { return this.usersTab === 'beta' ? this.userColumns.filter(column => column.key !== 'isAdmin') : this.userColumns; }
  private dateUtc(value: string) { return /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z'; }
  formatUserExpiry(value: string): string {
    const remaining = value ? new Date(value).getTime() - Date.now() : NaN;
    if (!Number.isFinite(remaining) || remaining <= 0) { return 'Wygas\u0142a'; }
    const days = Math.ceil(remaining / 86400000);
    return days === 1 ? 'za 1 dzie\u0144' : 'za ' + days + ' dni';
  }
  get userRows() {
    const now = Date.now();
    const rows = this.users.map(user => {
      const plans = (user.plans || []).slice().sort((a, b) => new Date(this.dateUtc(b.expiresAtUtc)).getTime() - new Date(this.dateUtc(a.expiresAtUtc)).getTime());
      const plan = plans[0];
      const expiresAtUtc = plan && plan.expiresAtUtc ? this.dateUtc(plan.expiresAtUtc) : '';
      return { id: user.id, displayName: user.displayName || '', googleEmail: user.googleEmail,
        disablePlanPurchase: !!user.disablePlanPurchase,
        isEnabled: !!user.isEnabled,
        isAdmin: !!user.isAdmin,
        isBetaUser: !!user.isBetaUser,
        createdAtUtc: user.createdAtUtc ? this.dateUtc(user.createdAtUtc) : '',
        planCode: plan ? plan.planCode : '', descriptionHtml: plan ? plan.descriptionHtml || '' : '', expiresAtUtc,
        expired: !expiresAtUtc || !Number.isFinite(new Date(expiresAtUtc).getTime()) || new Date(expiresAtUtc).getTime() <= now };
    });
    const email = this.userEmailFilter.trim().toLowerCase();
    return rows.filter(row => (this.usersTab === 'all' || (this.usersTab === 'beta' ? row.isBetaUser : row.isAdmin))
      && (!email || row.googleEmail.toLowerCase().includes(email))).sort((a, b) => {
      let comparison = 0;
      if (this.usersSortBy === 'expiresAtUtc' || this.usersSortBy === 'createdAtUtc') {
        const first = a[this.usersSortBy], second = b[this.usersSortBy];
        if (!first || !second) { return first ? -1 : second ? 1 : a.id - b.id; }
        comparison = new Date(first).getTime() - new Date(second).getTime();
      } else {
        comparison = String(a[this.usersSortBy]).localeCompare(String(b[this.usersSortBy]), 'pl');
      }
      return (this.usersDirection === 'asc' ? comparison : -comparison) || a.id - b.id;
    });
  }
  userEmailFilter = '';
  trackUser(index: number, row: { id: number }) { return row.id; }
  planHint: { html: string; left: number; top: number } | null = null;
  showPlanHint(event: Event, html: string) {
    if (!html) { this.planHint = null; return; }
    const bounds = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.planHint = { html,
      left: Math.max(12, Math.min(bounds.left, window.innerWidth - Math.min(420, window.innerWidth - 24) - 12)),
      top: Math.max(12, Math.min(bounds.bottom + 8, window.innerHeight - Math.min(320, window.innerHeight - 24) - 12)) };
  }
  sortUsers(column: string) {
    if (this.usersLoading || !this.userColumns.some(c => c.key === column)) { return; }
    this.usersDirection = this.usersSortBy === column && this.usersDirection === 'asc' ? 'desc' : 'asc';
    this.usersSortBy = column;
    this.changeUsersPage(1);
  }
  businessTypes: BusinessType[] = [];
  allPkds: Pkd[] = [];
  pageSize = 10;
  page = 1;
  total = 0;
  loading = false;
  selectedCompanyId: number | null = null;
  detailsCompany: Company | null = null;
  rodoSavingIds = new Set<number>();
  private rodoRequests = new Set<Subscription>();
  toggleRodo(company: Company) {
    if (this.rodoSavingIds.has(company.id)) { return; }
    this.rodoSavingIds.add(company.id);
    const request = this.api.updateCompanyRodo(company.id, !company.blockedByRODO)
      .pipe(finalize(() => this.rodoSavingIds.delete(company.id))).subscribe(result => {
        company.blockedByRODO = result.blockedByRODO;
        const current = this.companies.find(c => c.id === company.id);
        if (current) { current.blockedByRODO = result.blockedByRODO; }
      }, () => this.message = 'Nie udalo sie zmienic ustawienia RODO.');
    this.rodoRequests.add(request);
  }
  formatNumber(value: number) { return new Intl.NumberFormat('pl-PL').format(value); }
  formatPhone(value: string | null | undefined) { return this.api.formatPhone(value); }
  emailUrl(email: string) { return this.api.emailUrl(email); }
  websiteHref(website: string) { return this.api.websiteHref(website); }
  fullAddress(company: Company) {
    const number = (company.buildingNumber || '') + (company.apartmentNumber ? '/' + company.apartmentNumber : '');
    return [company.city, company.postalCode, company.street, number].filter(Boolean).join(' ');
  }
  filter = '';
  selectedPkds: Pkd[] = [];
  pkdTerm = '';
  pkdSuggestions: Pkd[] = [];
  maxSelectedPkds = 20;
  foundPkdId: number | null = null;
  selectedPkdId: number | null = null;
  sortBy = 'startedAt';
  direction = 'desc';
  message = '';
  planCode = 'PX5';

  companyColumns: SortableColumn[] = [
    { key: 'name', label: 'Nazwa' }, { key: 'nip', label: 'NIP' },
    { key: 'fullAddress', label: 'Adres' }, { key: 'phone', label: 'Telefon' },
    { key: 'email', label: 'Email' }, { key: 'website', label: 'Witryna WWW' },
    { key: 'startedAt', label: 'Dzia\u0142alno\u015b\u0107 od' },
    { key: 'dataModifiedOn', label: 'Zmodyfikowano' }
  ];

  maintenanceMode = false;
  @Input() publicPageTitle = '';
  constructor(public api: ApiService, private route: ActivatedRoute, private router: Router) {
    this.maintenanceMode = this.api.takeAdminMaintenanceNotice();
  }

  openAdminTab(tab: string) {
    if (!this.isAdminTab(tab)) { return; }
    this.tab = tab;
    try { localStorage.setItem(this.adminTabStorageKey, tab); } catch {}
    this.router.navigate(['/admin'], { queryParams: { tab }, replaceUrl: !this.publicPageTitle });
  }

  private get adminTabStorageKey(): string {
    const user = this.api.user();
    return 'coldcall.admin.lastTab.' + (user ? user.id : 'default');
  }

  private isAdminTab(tab: string): tab is AdminDashboardComponent['tab'] {
    return ['companies', 'users', 'payments', 'plans', 'imports', 'global-settings', 'regulamin', 'regulamin-beta', 'logins'].includes(tab);
  }

  ngOnInit() {
    if (!this.api.isAdmin()) {
      this.router.navigateByUrl(this.api.isAuthenticated() ? '/dashboard' : '/', { replaceUrl: true });
      return;
    }
    if (this.publicPageTitle) { return; }
    let savedTab = '';
    try { savedTab = localStorage.getItem(this.adminTabStorageKey) || ''; } catch {}
    const requestedTab = this.route.snapshot.queryParams.tab;
    const tab = this.isAdminTab(requestedTab) ? requestedTab : savedTab;
    if (this.isAdminTab(tab)) {
      this.tab = tab;
      try { localStorage.setItem(this.adminTabStorageKey, tab); } catch {}
    }
    this.loadCompanyStatuses();
    this.loadPkds();
    this.loadAddressOptions(0);
    this.loadCompanies();
    this.loadUsers();
  }

  loadCompanies(refreshCount = true, startedAt = Date.now()) {
    if (this.companiesDestroyed) { return; }
    this.companySearchError = '';
    this.searchStartedAt = startedAt;
    this.searchFinishedAt = 0;
    if (this.companiesRequest && !this.companiesRequest.closed) {
      this.pendingCompaniesLoad = { refresh: refreshCount || !!(this.pendingCompaniesLoad && this.pendingCompaniesLoad.refresh), startedAt };
      return;
    }
    this.loading = true;
    this.companiesRequest = this.api.adminCompanies(this.page, this.sortBy, this.direction, '', this.selectedPkds.map(p => p.id), this.filters, this.nipTerm.replace(/[-\s]/g, ''), this.blockedByRodoFilter, refreshCount).pipe(finalize(() => {
      this.loading = false;
      this.companiesRequest = null;
      const pending = this.pendingCompaniesLoad;
      this.pendingCompaniesLoad = null;
      if (pending && !this.companiesDestroyed) { this.loadCompanies(pending.refresh, pending.startedAt); }
    })).subscribe(x => {
      if (this.pendingCompaniesLoad || this.companiesDestroyed) { return; }
      this.searchFinishedAt = Date.now();
      this.companies = x.items;
      this.total = x.total;
      this.pageSize = x.pageSize;
    }, err => {
      if (this.pendingCompaniesLoad || this.companiesDestroyed) { return; }
      this.companySearchError = err.error && err.error.message ? err.error.message : 'Nie udalo sie pobrac firm.';
    });
  }

  loadUsers(highlightUserId?: number) {
    this.usersLoading = true;
    this.api.users().pipe(finalize(() => this.usersLoading = false)).subscribe(x => {
      this.users = x;
      if (highlightUserId !== undefined) {
        const position = this.userRows.findIndex(row => row.id === highlightUserId);
        if (position >= 0) {
          this.selectedUserId = highlightUserId;
          this.changeUsersPage(Math.floor(position / this.usersPageSize) + 1);
          return;
        }
      }
      this.changeUsersPage(this.usersPage);
    },
      () => this.message = 'Nie udalo sie pobrac uzytkownikow.');
  }

  loadBusinessTypes() { this.api.businessTypes().subscribe(x => this.businessTypes = x); }

  loadPkds() {
    this.api.pkd().subscribe(x => {
      this.allPkds = this.sortPkds(x);
      this.updatePkdSuggestions();
    });
  }

  findPkd() {
    this.updatePkdSuggestions();
  }

  addSelectedFoundPkd() {
    if (this.selectedPkds.length >= this.maxSelectedPkds) { return; }

    const pkd = this.pkdSuggestions.find(x => x.id === Number(this.foundPkdId));
    if (!pkd || this.selectedPkds.some(x => x.id === pkd.id)) { return; }

    this.selectedPkds = this.sortPkds(this.selectedPkds.concat(pkd));
    this.selectedPkdId = pkd.id;
    this.pkdSuggestions = this.pkdSuggestions.filter(x => x.id !== pkd.id);
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
    this.page = 1;
    this.loadCompanies();
  }

  removeSelectedPkd() {
    this.selectedPkds = this.selectedPkds.filter(x => x.id !== Number(this.selectedPkdId));
    this.selectedPkdId = this.selectedPkds.length ? this.selectedPkds[0].id : null;
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  clearPkds() {
    if (!this.selectedPkds.length) { return; }
    this.selectedPkds = [];
    this.selectedPkdId = null;
    this.page = 1;
    this.loadCompanies();
    this.updatePkdSuggestions();
  }

  sort(column: string) {
    if (this.loading) { return; }
    this.direction = this.sortBy === column && this.direction === 'asc' ? 'desc' : 'asc';
    this.sortBy = column;
    this.page = 1;
    this.loadCompanies(false);
  }

  grant(user: AppUser) {
    this.api.grantPlan(user.id, this.planCode).subscribe(() => {
      this.message = 'Plan zostal przyznany i wyslano email albo wpisano log, jesli SMTP nie jest skonfigurowane.';
      this.loadUsers();
    });
  }

  totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); }
  next() { if (!this.loading && this.page < this.totalPages()) { this.page++; this.loadCompanies(false); } }
  prev() { if (!this.loading && this.page > 1) { this.page--; this.loadCompanies(false); } }
  last() { this.page = this.totalPages(); this.loadCompanies(false); }

  private sortPkds(pkds: Pkd[]) {
    return pkds.slice().sort((a, b) => this.pkdLabel(a).localeCompare(this.pkdLabel(b)));
  }

  private updatePkdSuggestions() {
    const term = this.pkdTerm.trim().toLowerCase();
    this.pkdSuggestions = this.allPkds.filter(pkd => {
      const isAlreadySelected = this.selectedPkds.some(selected => selected.id === pkd.id);
      return !isAlreadySelected && (!term || this.pkdLabel(pkd).toLowerCase().indexOf(term) >= 0);
    });
    this.foundPkdId = this.pkdSuggestions.length ? this.pkdSuggestions[0].id : null;
  }

  pkdLabel(pkd: Pkd) {
    return `${pkd.code} ${pkd.name}`;
  }
}
