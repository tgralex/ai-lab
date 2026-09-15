import { Component, EventEmitter, Input, Output, computed, signal, SimpleChanges, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProviderModel, ObservedModelStatistics } from '../core/models';
import { formatMs } from './format';
import { ProviderIcon } from './provider-icon';
import { ClickOutsideDirective } from './click-outside.directive';

const PROVIDER_DISPLAY_NAMES: Record<string, string> = {
  openai: 'OpenAI',
  anthropic: 'Anthropic',
  grok: 'Grok',
  gemini: 'Gemini',
};

export interface ModelRef {
  providerId: string;
  modelId: string;
}

@Component({
  selector: 'app-model-dropdown',
  standalone: true,
  imports: [CommonModule, ProviderIcon, ClickOutsideDirective],
  templateUrl: './model-dropdown.html',
})
export class ModelDropdown implements OnChanges {
  @Input() models: ProviderModel[] = [];
  @Input() observedStats: ObservedModelStatistics[] = [];
  @Input() providerId: string | null = null;
  @Input() modelId: string | null = null;
  /** When true, renders checkboxes and stays open across picks instead of single-select. */
  @Input() multiple = false;
  @Input() selectedModels: ModelRef[] = [];
  @Output() modelChange = new EventEmitter<{ providerId: string; modelId: string }>();
  @Output() selectionChange = new EventEmitter<ModelRef[]>();

  readonly formatMs = formatMs;

  open = signal(false);
  providerFilter = signal<string | null>(null);
  showOlder = signal(false);
  private modelsSig = signal<ProviderModel[]>([]);
  private observedStatsSig = signal<ObservedModelStatistics[]>([]);
  private providerIdSig = signal<string | null>(null);
  private modelIdSig = signal<string | null>(null);
  private selectedModelsSig = signal<ModelRef[]>([]);

  ngOnChanges(changes: SimpleChanges) {
    if (changes['models']) this.modelsSig.set(this.models);
    if (changes['observedStats']) this.observedStatsSig.set(this.observedStats);
    if (changes['providerId']) this.providerIdSig.set(this.providerId);
    if (changes['modelId']) this.modelIdSig.set(this.modelId);
    if (changes['selectedModels']) this.selectedModelsSig.set(this.selectedModels);
  }

  observedFor(m: ProviderModel): ObservedModelStatistics | null {
    return this.observedStatsSig().find(s => s.providerId === m.providerId && s.modelId === m.modelId) ?? null;
  }

  selected = computed(() => this.modelsSig().find(m => m.providerId === this.providerIdSig() && m.modelId === this.modelIdSig()) ?? null);

  availableProviders = computed(() => Array.from(new Set(this.modelsSig().map(m => m.providerId))));

  providerDisplayName(id: string): string {
    return PROVIDER_DISPLAY_NAMES[id] ?? id.charAt(0).toUpperCase() + id.slice(1);
  }

  groupedModels = computed(() => {
    const filter = this.providerFilter();
    const showOlder = this.showOlder();
    const byProvider = new Map<string, ProviderModel[]>();
    for (const m of this.modelsSig()) {
      if (filter && m.providerId !== filter) continue;
      if (m.isDeprecated && !showOlder && !this.isPinnedVisible(m)) continue;
      const list = byProvider.get(m.providerId) ?? [];
      list.push(m);
      byProvider.set(m.providerId, list);
    }
    return Array.from(byProvider.entries()).map(([providerId, models]) => ({ providerId, models }));
  });

  /** Older-models count currently hidden by the provider filter, so "Show N older models" stays accurate as the filter changes. */
  hiddenOlderCount = computed(() => {
    const filter = this.providerFilter();
    return this.modelsSig().filter(m => m.isDeprecated && (!filter || m.providerId === filter) && !this.isPinnedVisible(m)).length;
  });

  toggleShowOlder() {
    this.showOlder.set(!this.showOlder());
  }

  // A saved request pointing at a since-deprecated snapshot shouldn't appear to vanish from its
  // own picker — always show whatever is currently selected regardless of the older-models filter.
  private isPinnedVisible(m: ProviderModel): boolean {
    if (this.multiple) {
      return this.isSelected(m);
    }

    return m.providerId === this.providerIdSig() && m.modelId === this.modelIdSig();
  }

  toggleOpen() {
    if (!this.open()) {
      // Default the filter to the currently-selected model's provider so re-opening the picker
      // for an existing OpenAI request starts narrowed to OpenAI, per the user's ask.
      this.providerFilter.set(this.providerIdSig());
    }
    this.open.set(!this.open());
  }

  close() {
    this.open.set(false);
  }

  setProviderFilter(id: string | null) {
    this.providerFilter.set(id);
  }

  isSelected(m: ProviderModel): boolean {
    return this.selectedModelsSig().some(s => s.providerId === m.providerId && s.modelId === m.modelId);
  }

  pick(m: ProviderModel) {
    if (this.multiple) {
      this.toggleMulti(m);
      return;
    }

    this.open.set(false);
    this.modelChange.emit({ providerId: m.providerId, modelId: m.modelId });
  }

  private toggleMulti(m: ProviderModel) {
    const current = this.selectedModelsSig();
    const exists = current.some(s => s.providerId === m.providerId && s.modelId === m.modelId);
    const next = exists
      ? current.filter(s => !(s.providerId === m.providerId && s.modelId === m.modelId))
      : [...current, { providerId: m.providerId, modelId: m.modelId }];
    this.selectedModelsSig.set(next);
    this.selectionChange.emit(next);
  }
}
