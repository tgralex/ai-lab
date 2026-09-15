import { Directive, ElementRef, EventEmitter, HostListener, Output, inject } from '@angular/core';

/**
 * Emits when a click lands outside the host element — put the directive on the element that
 * wraps BOTH the toggle trigger and the popover/dropdown content, so a click on the trigger
 * itself stays "inside" and doesn't fight with the trigger's own (click) toggle handler.
 */
@Directive({
  selector: '[appClickOutside]',
  standalone: true,
})
export class ClickOutsideDirective {
  @Output() appClickOutside = new EventEmitter<void>();

  private readonly el = inject(ElementRef<HTMLElement>).nativeElement;

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!this.el.contains(event.target as Node)) {
      this.appClickOutside.emit();
    }
  }
}
