import { Component, Input, computed, signal, SimpleChanges, OnChanges } from '@angular/core';

/**
 * Each provider's own site (claude.ai, chatgpt.com, grok.com) sends
 * `Cross-Origin-Resource-Policy: same-origin` on their favicon assets, which blocks direct
 * hotlinking from this app (verified — the request fails in-browser even though it succeeds via
 * curl, since CORP is enforced client-side). Google's favicon-resolution service fetches each
 * domain's current favicon and re-serves it without that restriction, so this still references
 * the real, official icon and updates automatically if a provider changes theirs — it's just
 * fetched via an intermediary rather than hotlinked directly.
 */
const PROVIDER_ICON_DOMAINS: Record<string, string> = {
  anthropic: 'claude.ai',
  openai: 'chatgpt.com',
  grok: 'grok.com',
};

function faviconUrl(domain: string): string {
  return `https://www.google.com/s2/favicons?sz=64&domain=${domain}`;
}

@Component({
  selector: 'app-provider-icon',
  standalone: true,
  host: {
    style: 'display: inline-flex; align-items: center; justify-content: center; line-height: 0;',
  },
  template: `
    @if (url() && !failed()) {
      <img [src]="url()" [alt]="providerId" [attr.class]="cls()" (error)="failed.set(true)" />
    }
  `,
})
export class ProviderIcon implements OnChanges {
  @Input() providerId = '';
  @Input() class = 'h-4 w-4';

  private providerIdSig = signal('');
  failed = signal(false);

  url = computed(() => {
    const domain = PROVIDER_ICON_DOMAINS[this.providerIdSig()];
    return domain ? faviconUrl(domain) : null;
  });

  ngOnChanges(changes: SimpleChanges) {
    if (changes['providerId']) {
      this.providerIdSig.set(this.providerId);
      this.failed.set(false);
    }
  }

  cls(): string {
    return `${this.class} shrink-0 block rounded-sm`;
  }
}
