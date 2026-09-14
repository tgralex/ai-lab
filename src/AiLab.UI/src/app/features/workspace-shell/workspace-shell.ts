import { Component, inject, signal, computed, OnInit, OnDestroy, SecurityContext } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { marked } from 'marked';
import { ApiService, CreateRequestBody } from '../../core/api.service';
import {
  Workspace, AiRequestDefinition, ExecutionRun, ProviderModel, ExecutionStatus, ExecutionStatusLabel,
  BenchmarkResponse, ComparisonRow, AiStreamEvent, ExecutionPlan, ObservedModelStatistics, Attachment,
} from '../../core/models';
import { ModelDropdown } from '../../shared/model-dropdown';
import { PersistSizeDirective } from '../../shared/persist-size.directive';
import { Icon } from '../../shared/icon';
import { formatTimeSpan, formatCost, formatTokensPerSecond, timeSpanToMs } from '../../shared/format';

type Tab = 'response' | 'stats' | 'history' | 'compare';
type ResponseViewMode = 'rendered' | 'plain' | 'raw';

const MIN_PANEL_WIDTH = 200;
const MAX_PANEL_WIDTH = 700;

@Component({
  selector: 'app-workspace-shell',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ModelDropdown, PersistSizeDirective, Icon],
  templateUrl: './workspace-shell.html',
})
export class WorkspaceShell implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);

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
  attachments = signal<Attachment[]>([]);
  attachmentsById = computed(() => new Map(this.attachments().map(a => [a.id, a])));

  selectedRequest = signal<AiRequestDefinition | null>(null);
  draft = signal<CreateRequestBody | null>(null);
  dirty = signal(false);
  uploadingCached = signal(false);
  uploadingUser = signal(false);

  activeTab = signal<Tab>('response');
  readonly tabs: Tab[] = ['response', 'stats', 'history', 'compare'];
  executing = signal(false);
  streamingOutput = signal('');
  currentRun = signal<ExecutionRun | null>(null);

  runs = signal<ExecutionRun[]>([]);
  selectedHistoryRun = signal<ExecutionRun | null>(null);

  benchmarkCount = signal(5);
  benchmarking = signal(false);
  benchmarkResult = signal<BenchmarkResponse | null>(null);

  compareSelection = signal<Set<string>>(new Set());
  comparisonRows = signal<ComparisonRow[]>([]);

  responseViewMode = signal<ResponseViewMode>('rendered');

  // --- Panel widths (px), resizable by dragging, persisted across reloads ---
  sidebarWidth = signal(loadPanelWidth('sidebar', 256));
  responsePanelWidth = signal(loadPanelWidth('response', 448));
  private resizingPanel: 'sidebar' | 'response' | null = null;
  private resizeStartX = 0;
  private resizeStartWidth = 0;
  private readonly onResizeMove = (e: MouseEvent) => this.handleResizeMove(e);
  private readonly onResizeUp = () => this.stopResize();

  // --- Execution timer ---
  elapsedMs = signal(0);
  private executionStartedAt = 0;
  private timerHandle: ReturnType<typeof setInterval> | null = null;

  displayOutput = computed(() => this.streamingOutput() || this.currentRun()?.output || this.selectedHistoryRun()?.output || '');

  isJsonOutput = computed(() => {
    const text = this.displayOutput().trim();
    if (!text) return false;
    try {
      JSON.parse(text);
      return true;
    } catch {
      return false;
    }
  });

  prettyJsonOutput = computed(() => {
    try {
      return JSON.stringify(JSON.parse(this.displayOutput()), null, 2);
    } catch {
      return this.displayOutput();
    }
  });

  renderedMarkdownHtml = computed(() => {
    const raw = marked.parse(this.displayOutput(), { async: false }) as string;
    return this.sanitizer.sanitize(SecurityContext.HTML, raw) ?? '';
  });

  async ngOnInit() {
    const workspaceId = this.route.snapshot.paramMap.get('workspaceId')!;
    this.workspaceId.set(workspaceId);
    await this.loadAll();
  }

  ngOnDestroy() {
    this.stopTimer();
    window.removeEventListener('mousemove', this.onResizeMove);
    window.removeEventListener('mouseup', this.onResizeUp);
  }

  startResize(panel: 'sidebar' | 'response', event: MouseEvent) {
    this.resizingPanel = panel;
    this.resizeStartX = event.clientX;
    this.resizeStartWidth = panel === 'sidebar' ? this.sidebarWidth() : this.responsePanelWidth();
    window.addEventListener('mousemove', this.onResizeMove);
    window.addEventListener('mouseup', this.onResizeUp);
    event.preventDefault();
  }

  private handleResizeMove(e: MouseEvent) {
    if (!this.resizingPanel) return;
    const delta = e.clientX - this.resizeStartX;
    // The response panel is on the right, so dragging it left (negative delta) should grow it.
    const signedDelta = this.resizingPanel === 'sidebar' ? delta : -delta;
    const newWidth = clamp(this.resizeStartWidth + signedDelta, MIN_PANEL_WIDTH, MAX_PANEL_WIDTH);
    if (this.resizingPanel === 'sidebar') {
      this.sidebarWidth.set(newWidth);
    } else {
      this.responsePanelWidth.set(newWidth);
    }
  }

  private stopResize() {
    if (this.resizingPanel === 'sidebar') savePanelWidth('sidebar', this.sidebarWidth());
    if (this.resizingPanel === 'response') savePanelWidth('response', this.responsePanelWidth());
    this.resizingPanel = null;
    window.removeEventListener('mousemove', this.onResizeMove);
    window.removeEventListener('mouseup', this.onResizeUp);
  }

  private startTimer() {
    this.executionStartedAt = Date.now();
    this.elapsedMs.set(0);
    this.timerHandle = setInterval(() => this.elapsedMs.set(Date.now() - this.executionStartedAt), 100);
  }

  private stopTimer() {
    if (this.timerHandle) {
      clearInterval(this.timerHandle);
      this.timerHandle = null;
    }
  }

  async loadAll() {
    const [workspace, requests, models, plans, observedStats, attachments] = await Promise.all([
      this.api.getWorkspace(this.workspaceId()),
      this.api.listRequests(this.workspaceId()),
      this.api.listModels(),
      this.api.listExecutionPlans(this.workspaceId()),
      this.api.observedModelStats(),
      this.api.listAttachments(this.workspaceId()),
    ]);
    this.workspace.set(workspace);
    this.requests.set(requests);
    this.models.set(models);
    this.plans.set(plans);
    this.observedStats.set(observedStats);
    this.attachments.set(attachments);
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

  // --- Attachments (cached/user context) ---

  async uploadFile(target: 'cached' | 'user', event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-selecting the same file later
    if (!file) return;

    const uploading = target === 'cached' ? this.uploadingCached : this.uploadingUser;
    uploading.set(true);
    try {
      const attachment = await this.api.uploadAttachment(this.workspaceId(), file);
      this.attachments.set([attachment, ...this.attachments()]);
      this.attachTo(target, attachment.id);
    } finally {
      uploading.set(false);
    }
  }

  attachExisting(target: 'cached' | 'user', event: Event) {
    const select = event.target as HTMLSelectElement;
    const attachmentId = select.value;
    select.value = '';
    if (!attachmentId) return;
    this.attachTo(target, attachmentId);
  }

  private attachTo(target: 'cached' | 'user', attachmentId: string) {
    const d = this.draft();
    if (!d) return;
    const key = target === 'cached' ? 'cachedContextAttachmentIds' : 'userContextAttachmentIds';
    const current = d[key] ?? [];
    if (current.includes(attachmentId)) return;
    this.draft.set({ ...d, [key]: [...current, attachmentId] });
    this.markDirty();
  }

  removeAttachment(target: 'cached' | 'user', attachmentId: string) {
    const d = this.draft();
    if (!d) return;
    const key = target === 'cached' ? 'cachedContextAttachmentIds' : 'userContextAttachmentIds';
    this.draft.set({ ...d, [key]: (d[key] ?? []).filter(id => id !== attachmentId) });
    this.markDirty();
  }

  attachmentLabel(id: string): string {
    const a = this.attachmentsById().get(id);
    return a ? `${a.filename} (${formatBytes(a.sizeBytes)})` : id;
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
    this.startTimer();

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
      this.stopTimer();
    }
  }

  async runBenchmark() {
    const selected = this.selectedRequest();
    if (!selected) return;
    this.benchmarking.set(true);
    this.startTimer();
    try {
      const result = await this.api.benchmark(selected.id, this.benchmarkCount());
      this.benchmarkResult.set(result);
      this.runs.set(await this.api.listRuns(selected.id));
    } finally {
      this.benchmarking.set(false);
      this.stopTimer();
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

  tabIcon(tab: Tab): string {
    switch (tab) {
      case 'response': return 'eye';
      case 'stats': return 'chartBar';
      case 'history': return 'clock';
      case 'compare': return 'scale';
    }
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
    cachedContextAttachmentIds: [...request.cachedContext.attachmentIds],
    userContextAttachmentIds: [...request.userContext.attachmentIds],
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

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}MB`;
}

function loadPanelWidth(key: string, fallback: number): number {
  try {
    const saved = localStorage.getItem(`ailab.panel-width.${key}`);
    return saved ? Number(saved) || fallback : fallback;
  } catch {
    return fallback;
  }
}

function savePanelWidth(key: string, width: number) {
  try {
    localStorage.setItem(`ailab.panel-width.${key}`, String(width));
  } catch {
    // Per-viewer convenience only — ignore storage failures.
  }
}
