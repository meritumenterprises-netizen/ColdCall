import { Component } from '@angular/core';

@Component({
  selector: 'app-lead-generation',
  templateUrl: './lead-generation.component.html',
  styles: [`:host { display: block; max-width: 1000px; line-height: 1.7; }
    h2 { font-size: 24px; margin: 28px 0 18px; }
    h2:first-child { margin-top: 0; }
    h3 { font-size: 20px; margin: 28px 0 14px; }
    h4 { font-size: 16px; margin: 22px 0 10px; }
    p { margin: 0 0 16px; } ul { padding-left: 24px; } li { margin-bottom: 12px; }
    a { overflow-wrap: anywhere; }`]
})
export class LeadGenerationComponent {}
