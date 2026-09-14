import { Component, EventEmitter, Input, Output, computed, signal, SimpleChanges, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProviderModel, ObservedModelStatistics } from '../core/models';
import { formatMs } from './format';

@Component({
  selector: 'app-model-dropdown',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './model-dropdown.html',
})
export class ModelDropdown implements OnChanges {
  @Input() models: ProviderModel[] = [];
  @Input() observedStats: ObservedModelStatistics[] = [];
  @Input() providerId: string | null = null;
  @Input() modelId: string | null = null;
  @Output() modelChange = new EventEmitter<{ providerId: string; modelId: string }>();

  readonly formatMs = formatMs;

  open = signal(false);
  private modelsSig = signal<ProviderModel[]>([]);
  private observedStatsSig = signal<ObservedModelStatistics[]>([]);
  private providerIdSig = signal<string | null>(null);
  private modelIdSig = signal<string | null>(null);

  ngOnChanges(changes: SimpleChanges) {
    if (changes['models']) this.modelsSig.set(this.models);
    if (changes['observedStats']) this.observedStatsSig.set(this.observedStats);
    if (changes['providerId']) this.providerIdSig.set(this.providerId);
    if (changes['modelId']) this.modelIdSig.set(this.modelId);
  }

  observedFor(m: ProviderModel): ObservedModelStatistics | null {
    return this.observedStatsSig().find(s => s.providerId === m.providerId && s.modelId === m.modelId) ?? null;
  }

  selected = computed(() => this.modelsSig().find(m => m.providerId === this.providerIdSig() && m.modelId === this.modelIdSig()) ?? null);

  groupedModels = computed(() => {
    const byProvider = new Map<string, ProviderModel[]>();
    for (const m of this.modelsSig()) {
      const list = byProvider.get(m.providerId) ?? [];
      list.push(m);
      byProvider.set(m.providerId, list);
    }
    return Array.from(byProvider.entries()).map(([providerId, models]) => ({ providerId, models }));
  });

  pick(m: ProviderModel) {
    this.open.set(false);
    this.modelChange.emit({ providerId: m.providerId, modelId: m.modelId });
  }
}
