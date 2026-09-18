import { AfterViewInit, Directive, ElementRef, inject } from '@angular/core';

/** Focuses and selects the host `<input>`'s text as soon as it's rendered — for inline-rename
 * fields that appear on demand (e.g. double-click a node's title) and should be ready to type
 * into immediately, with the existing text pre-selected for a quick full replace. */
@Directive({
  selector: '[appAutofocusSelect]',
  standalone: true,
})
export class AutofocusSelectDirective implements AfterViewInit {
  private readonly el = inject(ElementRef<HTMLInputElement>).nativeElement;

  ngAfterViewInit() {
    queueMicrotask(() => {
      this.el.focus();
      this.el.select();
    });
  }
}
