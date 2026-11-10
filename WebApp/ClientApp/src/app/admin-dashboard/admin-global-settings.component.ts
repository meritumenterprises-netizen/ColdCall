import { Component, EventEmitter, OnDestroy, OnInit, Output } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ApiService, AdminGlobalSetting } from '../services/api.service';
import { ANGULAR_APP_VERSION } from '../version';

interface SettingsRow extends AdminGlobalSetting {
  savedAppVersion: string;
  savedParallelism: number;
  savedInService: boolean;
  savedRegistrationSuspended: boolean;
  savedInFaktInvoicing: boolean;
  saving: boolean;
  error: string;
  saved: boolean;
}

@Component({
  selector: 'app-admin-global-settings',
  templateUrl: './admin-global-settings.component.html',
  styleUrls: ['./admin-global-settings.component.css']
})
export class AdminGlobalSettingsComponent implements OnInit, OnDestroy {
  readonly frontendVersion = ANGULAR_APP_VERSION;
  @Output() settingsSaved = new EventEmitter<void>();
  rows: SettingsRow[] = [];
  loading = false;
  error = '';
  private requests = new Subscription();

  constructor(private api: ApiService) {}
  ngOnInit() { this.load(); }
  ngOnDestroy() { this.requests.unsubscribe(); }
  load() {
    if (this.loading) { return; }
    this.loading = true;
    this.error = '';
    this.requests.add(this.api.adminGlobalSettings().pipe(finalize(() => this.loading = false)).subscribe(rows => {
      this.rows = rows.map(row => ({ ...row, appVersion: row.appVersion || '', savedAppVersion: row.appVersion || '', savedParallelism: row.globalDegreeOfParallelismImportByDate,
        savedInService: row.applicationInService, savedRegistrationSuspended: row.registrationSuspended,
        savedInFaktInvoicing: row.inFaktInvoicing, saving: false, error: '', saved: false }));
    }, () => this.error = 'Nie udalo sie pobrac ustawien.'));
  }
  changed(row: SettingsRow) {
    return (row.appVersion || '') !== row.savedAppVersion || row.globalDegreeOfParallelismImportByDate !== row.savedParallelism || row.applicationInService !== row.savedInService
      || row.registrationSuspended !== row.savedRegistrationSuspended || row.inFaktInvoicing !== row.savedInFaktInvoicing;
  }
  valid(row: SettingsRow) {
    return Number.isInteger(row.globalDegreeOfParallelismImportByDate)
      && row.globalDegreeOfParallelismImportByDate >= 1 && row.globalDegreeOfParallelismImportByDate <= 2147483647;
  }
  cancel(row: SettingsRow) {
    if (row.saving) { return; }
    row.appVersion = row.savedAppVersion;
    row.globalDegreeOfParallelismImportByDate = row.savedParallelism;
    row.applicationInService = row.savedInService;
    row.registrationSuspended = row.savedRegistrationSuspended;
    row.inFaktInvoicing = row.savedInFaktInvoicing;
    row.error = ''; row.saved = false;
  }
  save(row: SettingsRow) {
    if (row.saving || !this.changed(row) || !this.valid(row)) { return; }
    row.saving = true; row.error = ''; row.saved = false;
    const value: AdminGlobalSetting = { globalSettingsId: row.globalSettingsId,
      appVersion: (row.appVersion || '').trim(),
      globalDegreeOfParallelismImportByDate: row.globalDegreeOfParallelismImportByDate,
      applicationInService: row.applicationInService, registrationSuspended: row.registrationSuspended,
      inFaktInvoicing: row.inFaktInvoicing };
    this.requests.add(this.api.saveAdminGlobalSettings(value).pipe(finalize(() => row.saving = false)).subscribe(() => {
      row.savedParallelism = value.globalDegreeOfParallelismImportByDate;
      row.appVersion = value.appVersion;
      row.savedAppVersion = value.appVersion;
      row.savedInService = value.applicationInService;
      row.savedRegistrationSuspended = value.registrationSuspended;
      row.savedInFaktInvoicing = value.inFaktInvoicing;
      row.saved = true;
      this.settingsSaved.emit();
    }, () => row.error = 'Nie udalo sie zapisac ustawien.'));
  }
  trackRow(index: number, row: SettingsRow) { return row.globalSettingsId; }
}
