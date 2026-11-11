import { Component } from '@angular/core';

@Component({
  selector: 'app-accounting-offices',
  templateUrl: './accounting-offices.component.html',
  styles: [`:host { display: block; max-width: 1000px; line-height: 1.7; }
    h2 { font-size: 24px; margin: 0 0 24px; }
    h3 { font-size: 20px; margin: 28px 0 14px; }
    p { margin: 0 0 16px; } ul { padding-left: 24px; } li { margin-bottom: 12px; }
    a { overflow-wrap: anywhere; }`]
})
export class AccountingOfficesComponent {}
