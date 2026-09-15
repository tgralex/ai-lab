import { Component, Input, computed, signal, SimpleChanges, OnChanges } from '@angular/core';

const ICONS: Record<string, string> = {
  save: 'M17 21v-8H7v8M7 3v5h8M21 7.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11.5L21 7.5Z',
  play: 'M6 4.5v15l13-7.5-13-7.5Z',
  copy: 'M8 8V5a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-3M5 8h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2Z',
  trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M9 7V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v3',
  check: 'M5 13l4 4L19 7',
  x: 'M6 6l12 12M18 6L6 18',
  plus: 'M12 5v14M5 12h14',
  download: 'M12 3v12m0 0 4-4m-4 4-4-4M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2',
  upload: 'M12 21V9m0 0 4 4m-4-4-4 4M4 7V5a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v2',
  paperclip: 'M21.44 11.05 12.25 20.24a5 5 0 0 1-7.07-7.07l9.19-9.19a3.5 3.5 0 0 1 4.95 4.95L10.13 18.12a2 2 0 0 1-2.83-2.83l8.49-8.48',
  pencil: 'M11 5H6a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-5m-1.5-9.5a2.121 2.121 0 0 1 3 3L12 16l-4 1 1-4 9.5-9.5Z',
  refresh: 'M4 4v5h5M20 20v-5h-5M4.6 9A8 8 0 0 1 19.4 9M19.4 15a8 8 0 0 1-14.8 0',
  arrowLeft: 'M19 12H5m0 0 7 7m-7-7 7-7',
  chartBar: 'M4 20V10m6 10V4m6 16v-7',
  clock: 'M12 8v4l3 3m6-3a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
  scale: 'M12 3v18m0-18-5 4m5-4 5 4M3.5 9h5l-2.5 6-2.5-6Zm12 0h5l-2.5 6-2.5-6Z',
  eye: 'M2.5 12S6 5 12 5s9.5 7 9.5 7-3.5 7-9.5 7-9.5-7-9.5-7Z M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z',
  code: 'm9 8-4 4 4 4m6-8 4 4-4 4',
  doc: 'M7 3h7l5 5v13a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z M14 3v5h5 M9 13h6 M9 17h6',
  archive: 'M3 4h18v4H3z M4 8v11a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1V8 M10 12h4',
  unarchive: 'M3 4h18v4H3z M4 8v11a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1V8 M10 14l2-2 2 2 M12 12v5',
  spinner: '',
};

@Component({
  selector: 'app-icon',
  standalone: true,
  // Host is an inline-flex, centered box — this is what actually keeps the icon vertically
  // centered against adjacent button text, regardless of the icon's own size or the text's
  // line-height (relying on the child <svg>'s own display/vertical-align isn't enough, since
  // <app-icon> itself is an unstyled custom element and would otherwise lay out as plain `inline`).
  host: {
    style: 'display: inline-flex; align-items: center; justify-content: center; line-height: 0;',
  },
  template: `
    @if (isSpinner()) {
      <svg [attr.class]="cls()" viewBox="0 0 24 24" fill="none" class="animate-spin">
        <circle class="opacity-20" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="2.5"></circle>
        <path class="opacity-90" d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"></path>
      </svg>
    } @else {
      <svg [attr.class]="cls()" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path [attr.d]="path()"></path>
      </svg>
    }
  `,
})
export class Icon implements OnChanges {
  @Input() name = '';
  @Input() class = 'h-4 w-4';

  private nameSig = signal('');
  path = computed(() => ICONS[this.nameSig()] ?? '');
  isSpinner = computed(() => this.nameSig() === 'spinner');

  ngOnChanges(changes: SimpleChanges) {
    if (changes['name']) this.nameSig.set(this.name);
  }

  cls(): string {
    return `${this.class} shrink-0 block`;
  }
}
