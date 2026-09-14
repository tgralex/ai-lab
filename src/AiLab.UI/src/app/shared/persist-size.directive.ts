import { Directive, ElementRef, Input, OnDestroy, OnInit, inject } from '@angular/core';

/**
 * Remembers a resizable <textarea>'s size across reloads, keyed by a caller-supplied name
 * (e.g. "systemPrompt"). Uses ResizeObserver so any resize — drag-handle or programmatic — sticks.
 */
@Directive({
  selector: '[appPersistSize]',
  standalone: true,
})
export class PersistSizeDirective implements OnInit, OnDestroy {
  @Input('appPersistSize') key = '';

  private readonly el = inject(ElementRef<HTMLElement>).nativeElement;
  private observer: ResizeObserver | null = null;
  private saveTimeout: ReturnType<typeof setTimeout> | null = null;

  ngOnInit() {
    if (!this.key) return;

    try {
      const saved = localStorage.getItem(this.storageKey());
      if (saved) {
        const { width, height } = JSON.parse(saved);
        if (width) this.el.style.width = width;
        if (height) this.el.style.height = height;
      }
    } catch {
      // Corrupt/blocked storage — fall back to default size silently.
    }

    this.observer = new ResizeObserver(() => this.scheduleSave());
    this.observer.observe(this.el);
  }

  ngOnDestroy() {
    this.observer?.disconnect();
    if (this.saveTimeout) clearTimeout(this.saveTimeout);
  }

  private scheduleSave() {
    if (this.saveTimeout) clearTimeout(this.saveTimeout);
    this.saveTimeout = setTimeout(() => {
      try {
        localStorage.setItem(this.storageKey(), JSON.stringify({ width: this.el.style.width, height: this.el.style.height }));
      } catch {
        // Ignore — per-viewer convenience only.
      }
    }, 300);
  }

  private storageKey(): string {
    return `ailab.textarea-size.${this.key}`;
  }
}
