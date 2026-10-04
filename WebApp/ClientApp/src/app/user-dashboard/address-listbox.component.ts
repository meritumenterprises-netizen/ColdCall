import { Component, ElementRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild } from '@angular/core';
import { AddressOption } from '../services/api.service';

@Component({
  selector: 'app-address-listbox',
  template: `
    <div #viewport class="viewport" role="listbox" [id]="listId" [attr.aria-label]="label"
         [attr.tabindex]="disabled ? -1 : 0" [attr.aria-disabled]="disabled" [attr.aria-busy]="loading"
         [attr.aria-activedescendant]="activeOptionId" (scroll)="updateWindow()" (keydown)="navigate($event)">
      <div class="spacer" [style.height.px]="options.length * rowHeight">
        <div class="rows" [style.transform]="'translateY(' + start * rowHeight + 'px)'">
          <div *ngFor="let option of rendered; let i = index; trackBy: optionId" class="option" role="option"
               [id]="listId + '-' + option.id" [attr.aria-selected]="selectedId === option.id"
               [attr.aria-setsize]="options.length" [attr.aria-posinset]="start + i + 1"
               [class.selected]="selectedId === option.id" [attr.title]="option.name"
               (click)="choose(option.id)">{{ option.name }}</div>
        </div>
      </div>
    </div>`,
  styles: [`
    :host { display: block; min-width: 0; }
    .viewport { height: 202px; box-sizing: border-box; overflow-y: auto; overflow-x: hidden;
      border: 1px solid #cbd5e1; border-radius: 4px; background: white; font-size: 14px; }
    .viewport:focus-visible { outline: 2px solid #087f5b; outline-offset: 1px; }
    .viewport[aria-disabled="true"] { background: #e2e8f0; color: #64748b; }
    .spacer { position: relative; }
    .rows { position: absolute; top: 0; left: 0; width: 100%; }
    .option { height: 26px; box-sizing: border-box; line-height: 26px; padding: 0 6px;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: default; }
    .viewport[aria-disabled="false"] .option:hover { background: #f0f7f4; }
    .option.selected { background: #d9eee5; color: #153c2c; }
  `]
})
export class AddressListboxComponent implements OnChanges {
  @Input() options: AddressOption[] = [];
  @Input() selectedId: number | null = null;
  @Input() disabled = false;
  @Input() loading = false;
  @Input() label = '';
  @Input() listId = '';
  @Output() selectionChange = new EventEmitter<number>();
  @ViewChild('viewport', { static: true }) viewport!: ElementRef<HTMLElement>;
  readonly rowHeight = 26;
  start = 0;
  rendered: AddressOption[] = [];
  private typed = '';
  private lastTypedAt = 0;

  ngOnChanges(changes: SimpleChanges) {
    if (changes.options) {
      this.viewport.nativeElement.scrollTop = 0;
      this.typed = '';
      this.updateWindow(true);
    }
  }

  get activeOptionId() {
    return this.rendered.some(option => option.id === this.selectedId) ? this.listId + '-' + this.selectedId : null;
  }

  optionId(index: number, option: AddressOption) { return option.id; }

  updateWindow(force = false) {
    // Bound Angular views even when the full dictionary contains tens of thousands of items.
    const start = Math.max(0, Math.floor(this.viewport.nativeElement.scrollTop / this.rowHeight) - 4);
    if (!force && start === this.start) { return; }
    this.start = start;
    this.rendered = this.options.slice(start, start + 24);
  }

  choose(id: number) {
    if (!this.disabled) { this.selectionChange.emit(id); }
  }

  navigate(event: KeyboardEvent) {
    if (this.disabled || !this.options.length) { return; }
    const current = this.options.findIndex(option => option.id === this.selectedId);
    let index: number;
    if (event.key === 'Home') { index = 0; }
    else if (event.key === 'End') { index = this.options.length - 1; }
    else if (['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp'].includes(event.key)) {
      const step = event.key.startsWith('Page') ? 7 : 1;
      const direction = event.key === 'ArrowDown' || event.key === 'PageDown' ? 1 : -1;
      index = current < 0 ? (direction > 0 ? 0 : this.options.length - 1) : current + direction * step;
    } else if (event.key.length === 1 && !event.ctrlKey && !event.altKey && !event.metaKey) {
      const now = Date.now();
      this.typed = (now - this.lastTypedAt < 600 ? this.typed : '') + event.key.toLowerCase();
      this.lastTypedAt = now;
      index = this.options.findIndex(option => option.name.toLowerCase().startsWith(this.typed));
      if (index < 0) { return; }
    } else { return; }
    event.preventDefault();
    index = Math.max(0, Math.min(this.options.length - 1, index));
    const element = this.viewport.nativeElement;
    const top = index * this.rowHeight;
    if (top < element.scrollTop) { element.scrollTop = top; }
    else if (top + this.rowHeight > element.scrollTop + element.clientHeight) {
      element.scrollTop = top + this.rowHeight - element.clientHeight;
    }
    this.updateWindow(true);
    this.choose(this.options[index].id);
  }
}
