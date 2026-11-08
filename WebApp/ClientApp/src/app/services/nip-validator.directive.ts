import { Directive, forwardRef, Input } from '@angular/core';
import { AbstractControl, NG_VALIDATORS, ValidationErrors, Validator } from '@angular/forms';
import { isValidNip } from './nip-validation';

@Directive({
  selector: '[appNip]',
  providers: [{ provide: NG_VALIDATORS, useExisting: forwardRef(() => NipValidatorDirective), multi: true }]
})
export class NipValidatorDirective implements Validator {
  @Input() appNip = true;
  validate(control: AbstractControl): ValidationErrors | null {
    return !this.appNip || isValidNip(control.value) ? null : { nip: true };
  }
}
