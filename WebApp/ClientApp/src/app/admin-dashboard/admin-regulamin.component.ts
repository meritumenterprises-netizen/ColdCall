import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { ApiService } from '../services/api.service';

@Component({
  selector: 'app-admin-regulamin',
  templateUrl: './admin-regulamin.component.html',
  styleUrls: ['./admin-regulamin.component.css']
})
export class AdminRegulaminComponent implements OnInit, OnDestroy {
  activeTab: 'preview' | 'html' = 'preview';
  loading = false;
  saving = false;
  error = '';
  message = '';
  text = '';
  private savedText = '';
  private settingsId: number | null = null;
  private loadRequest: Subscription | null = null;
  private saveRequest: Subscription | null = null;
  constructor(private api: ApiService) {}
  get dirty() { return this.settingsId !== null && this.text !== this.savedText; }
  ngOnInit() { this.load(); }
  load() {
    if (this.loadRequest) { this.loadRequest.unsubscribe(); }
    this.loading = true; this.error = ''; this.message = '';
    this.loadRequest = this.api.adminRegulamin().pipe(finalize(() => this.loading = false)).subscribe(row => {
      this.settingsId = row.globalSettingsId;
      this.text = this.savedText = row.regulamin || '';
    }, () => this.error = 'Nie udalo sie pobrac regulaminu.');
  }
  save() {
    if (!this.dirty || this.saving || this.settingsId === null) { return; }
    const text = this.text;
    this.saving = true; this.error = ''; this.message = '';
    this.saveRequest = this.api.saveAdminRegulamin(this.settingsId, text).pipe(finalize(() => this.saving = false)).subscribe(() => {
      this.savedText = text; this.message = 'Regulamin zostal zapisany.';
    }, () => this.error = 'Nie udalo sie zapisac regulaminu. Sprobuj ponownie.');
  }
  cancel() { if (!this.saving) { this.text = this.savedText; this.error = ''; this.message = ''; } }
  navigateTabs(event: KeyboardEvent) {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) { return; }
    event.preventDefault();
    this.activeTab = event.key === 'Home' ? 'preview' : event.key === 'End' ? 'html' : this.activeTab === 'preview' ? 'html' : 'preview';
    const target = (event.currentTarget as HTMLElement).querySelector<HTMLButtonElement>('[data-tab="' + this.activeTab + '"]');
    if (target) { target.focus(); }
  }
  ngOnDestroy() {
    if (this.loadRequest) { this.loadRequest.unsubscribe(); }
    if (this.saveRequest) { this.saveRequest.unsubscribe(); }
  }
}
