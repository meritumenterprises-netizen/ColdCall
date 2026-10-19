import { ChangeDetectorRef, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild } from '@angular/core';

@Component({
  selector: 'app-company-notes-indicator',
  templateUrl: './company-notes-indicator.component.html',
  styleUrls: ['./company-notes-indicator.component.css']
})
export class CompanyNotesIndicatorComponent implements OnDestroy {
  private static nextId = 0;
  readonly tooltipId = 'company-notes-tooltip-' + (++CompanyNotesIndicatorComponent.nextId);
  private text = '';
  lines: string[] = [];
  visible = false;
  left = 8;
  top = 8;
  @ViewChild('tooltip', { static: false }) tooltip: ElementRef;
  @Output() opened = new EventEmitter<void>();

  @Input() set notes(value: string | null | undefined) {
    this.text = value || '';
    this.lines = this.text.split(/\r\n|[\r\n\u0085\u2028\u2029]/);
    this.hide();
  }
  get hasNotes() { return !!this.text.trim(); }

  private hideOnScroll = () => this.hide();

  constructor(private changeDetector: ChangeDetectorRef) {
    document.addEventListener('scroll', this.hideOnScroll, true);
    window.addEventListener('resize', this.hideOnScroll);
  }

  show(event: Event) {
    if (!this.hasNotes) { return; }
    const anchor = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.visible = true;
    this.left = 8;
    this.top = 8;
    this.changeDetector.detectChanges();
    const hint = (this.tooltip.nativeElement as HTMLElement).getBoundingClientRect();
    this.left = Math.max(8, Math.min(anchor.left, window.innerWidth - hint.width - 8));
    this.top = anchor.bottom + 6 + hint.height <= window.innerHeight - 8
      ? anchor.bottom + 6 : Math.max(8, anchor.top - hint.height - 6);
  }

  hide() { this.visible = false; }

  openNotes() {
    this.hide();
    this.opened.emit();
  }

  ngOnDestroy() {
    document.removeEventListener('scroll', this.hideOnScroll, true);
    window.removeEventListener('resize', this.hideOnScroll);
  }
}
