import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService, CreateRequestBody } from '../../core/api.service';
import {
  Workspace, AiRequestDefinition, ExecutionRun, ProviderModel, ExecutionStatus, ExecutionStatusLabel,
  BenchmarkResponse, ComparisonRow, AiStreamEvent, ExecutionPlan, ObservedModelStatistics,
} from '../../core/models';
import { ModelDropdown } from '../../shared/model-dropdown';
import { formatTimeSpan, formatCost, formatTokensPerSecond, timeSpanToMs } from '../../shared/format';

type Tab = 'response' | 'stats' | 'history' | 'compare';

@Component({
  selector: 'app-workspace-shell',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ModelDropdown],
  templateUrl: './workspace-shell.html',
})
export class WorkspaceShell implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly ExecutionStatusLabel = ExecutionStatusLabel;
  readonly formatTimeSpan = formatTimeSpan;
  readonly formatCost = formatCost;
  readonly formatTokensPerSecond = formatTokensPerSecond;

  workspaceId = signal<string>('');
  workspace = signal<Workspace | null>(null);
  requests = signal<AiRequestDefinition[]>([]);
  models = signal<ProviderModel[]>([]);
  observedStats = signal<ObservedModelStatistics[]>([]);
  plans = signal<ExecutionPlan[]>([]);

  selectedRequest = signal<AiRequestDefinition | null>(null);
  draft = signal<CreateRequestBody | null>(null);
  dirty = signal(false);

  activeTab = signal<Tab>('response');
  readonly tabs: Tab[] = ['response', 'stats', 'history', 'compare'];
  executing = signal(false);
  streamingOutput = signal('');
  currentRun = signal<ExecutionRun | null>(null);
  showRawJson = signal(false);

  runs = signal<ExecutionRun[]>([]);
  selectedHistoryRun = signal<ExecutionRun | null>(null);

  benchmarkCount = signal(5);
  benchmarking = signal(false);
  benchmarkResult = signal<BenchmarkResponse | null>(null);

  compareSelection = signal<Set<string>>(new Set());
  comparisonRows = signal<ComparisonRow[]>([]);

  displayOutput = computed(() => this.streamingOutput() || this.currentRun()?.output || this.selectedHistoryRun()?.output || '');

  async ngOnInit() {
    const workspaceId = this.route.snapshot.paramMap.get('workspaceId')!;
    this.workspaceId.set(workspaceId);
    await this.loadAll();
  }

  async loadAll() {
    const [workspace, requests, models, plans, observedStats] = await Promise.all([
      this.api.getWorkspace(this.workspaceId()),
      this.api.listRequests(this.workspaceId()),
      this.api.listModels(),
      this.api.listExecutionPlans(this.workspaceId()),
      this.api.observedModelStats(),
    ]);
    this.workspace.set(workspace);
    this.requests.set(requests);
    this.models.set(models);
    this.plans.set(plans);
    this.observedStats.set(observedStats);
  }

  async selectRequest(request: AiRequestDefinition) {
    this.selectedRequest.set(request);
    this.draft.set(toDraft(request));
    this.dirty.set(false);
    this.streamingOutput.set('');
    this.currentRun.set(null);
    this.selectedHistoryRun.set(null);
    this.benchmarkResult.set(null);
    this.activeTab.set('response');
    this.runs.set(await this.api.listRuns(request.id));
  }

  async createRequest() {
    const firstModel = this.models()[0];
    const body: CreateRequestBody = {
      name: this.nextDefaultName('New Request'),
      providerId: firstModel?.providerId ?? 'openai',
      modelId: firstModel?.modelId ?? '',
      userContextText: '',
      streamingEnabled: true,
    };
    const created = await this.api.createRequest(this.workspaceId(), body);
    this.requests.set([...this.requests(), created]);
    await this.selectRequest(created);
  }

  private nextDefaultName(base: string): string {
    const existing = new Set(this.requests().map(r => r.name));
    if (!existing.has(base)) return base;
    let i = 2;
    while (existing.has(`${base} ${i}`)) i++;
    return `${base} ${i}`;
  }

  markDirty() {
    this.dirty.set(true);
  }

  onModelChange(evt: { providerId: string; modelId: string }) {
    const d = this.draft();
    if (!d) return;
    this.draft.set({ ...d, providerId: evt.providerId, modelId: evt.modelId });
    this.markDirty();
  }

  async saveRequest() {
    const selected = this.selectedRequest();
    const draft = this.draft();
    if (!selected || !draft) return;
    const updated = await this.api.updateRequest(selected.id, draft);
    this.selectedRequest.set(updated);
    this.requests.set(this.requests().map(r => (r.id === updated.id ? updated : r)));
    this.dirty.set(false);
  }

  async cloneRequest() {
    const selected = this.selectedRequest();
    if (!selected) return;
    const clone = await this.api.cloneRequest(selected.id);
    this.requests.set([...this.requests(), clone]);
    await this.selectRequest(clone);
  }

  confirmingDelete = signal(false);

  requestDelete() {
    this.confirmingDelete.set(true);
  }

  cancelDelete() {
    this.confirmingDelete.set(false);
  }

  async confirmDelete() {
    const selected = this.selectedRequest();
    if (!selected) return;
    await this.api.deleteRequest(selected.id);
    this.requests.set(this.requests().filter(r => r.id !== selected.id));
    this.selectedRequest.set(null);
    this.draft.set(null);
    this.confirmingDelete.set(false);
  }

  async execute() {
    const selected = this.selectedRequest();
    if (!selected || this.dirty()) return;
    this.executing.set(true);
    this.streamingOutput.set('');
    this.currentRun.set(null);
    this.activeTab.set('response');

    try {
      if (selected.streamingEnabled) {
        const run = await this.api.executeRequestStream(selected.id, (evt: AiStreamEvent) => {
          if (evt.kind === 2 && evt.textDelta) {
            this.streamingOutput.set(this.streamingOutput() + evt.textDelta);
          }
        });
        this.currentRun.set(run);
      } else {
        const run = await this.api.executeRequest(selected.id);
        this.currentRun.set(run);
      }
      this.runs.set(await this.api.listRuns(selected.id));
      this.observedStats.set(await this.api.observedModelStats());
    } finally {
      this.executing.set(false);
    }
  }

  async runBenchmark() {
    const selected = this.selectedRequest();
    if (!selected) return;
    this.benchmarking.set(true);
    try {
      const result = await this.api.benchmark(selected.id, this.benchmarkCount());
      this.benchmarkResult.set(result);
      this.runs.set(await this.api.listRuns(selected.id));
    } finally {
      this.benchmarking.set(false);
    }
  }

  viewHistoryRun(run: ExecutionRun) {
    this.selectedHistoryRun.set(run);
    this.currentRun.set(null);
    this.streamingOutput.set('');
    this.activeTab.set('response');
  }

  toggleCompareSelection(id: string) {
    const set = new Set(this.compareSelection());
    if (set.has(id)) set.delete(id);
    else set.add(id);
    this.compareSelection.set(set);
  }

  async loadComparison() {
    const ids = Array.from(this.compareSelection());
    if (ids.length === 0) return;
    this.comparisonRows.set(await this.api.comparison(ids));
    this.activeTab.set('compare');
  }

  async exportCsv() {
    const csv = await this.api.exportWorkspace(this.workspaceId(), 'csv');
    downloadText(csv, `${this.workspace()?.name ?? 'workspace'}-export.csv`, 'text/csv');
  }

  private nextDefaultPlanName(): string {
    const existing = new Set(this.plans().map(p => p.name));
    if (!existing.has('New Plan')) return 'New Plan';
    let i = 2;
    while (existing.has(`New Plan ${i}`)) i++;
    return `New Plan ${i}`;
  }

  async createPlan() {
    const plan = await this.api.createExecutionPlan(this.workspaceId(), this.nextDefaultPlanName(), [], []);
    void this.router.navigate(['/w', this.workspaceId(), 'plans', plan.id]);
  }

  rawResponseJson = computed(() => {
    const run = this.currentRun() ?? this.selectedHistoryRun();
    if (!run?.rawProviderResponseJson) return '';
    try {
      return JSON.stringify(JSON.parse(run.rawProviderResponseJson), null, 2);
    } catch {
      return run.rawProviderResponseJson;
    }
  });

  statusLabel(status: ExecutionStatus): string {
    return ExecutionStatusLabel[status];
  }

  outputTokensPerSecondDisplay(run: ExecutionRun | null): string {
    if (!run?.outputTokensPerSecond) return '—';
    return formatTokensPerSecond(run.outputTokensPerSecond);
  }
}

function toDraft(request: AiRequestDefinition): CreateRequestBody {
  return {
    name: request.name,
    description: request.description,
    providerId: request.providerId,
    modelId: request.modelId,
    systemPrompt: request.systemPrompt,
    cachedContextText: request.cachedContext.text,
    userContextText: request.userContext.text,
    streamingEnabled: request.streamingEnabled,
    maxOutputTokens: request.maxOutputTokens,
    reasoningEffort: request.reasoning?.effort ?? null,
    promptCacheKey: request.promptCacheKey,
    structuredOutputSchema: request.structuredOutputSchema,
    tags: request.tags,
  };
}

function downloadText(content: string, filename: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
