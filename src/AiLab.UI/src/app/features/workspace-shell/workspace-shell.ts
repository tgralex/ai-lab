import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService, CreateRequestBody, CancelableExecution } from '../../core/api.service';
import {
  Workspace, AiRequestDefinition, ExecutionRun, ProviderModel, ExecutionStatus, ExecutionStatusLabel,
  BenchmarkResponse, ComparisonRow, AiStreamEvent, ExecutionPlan, ObservedModelStatistics, Attachment,
} from '../../core/models';
import { ModelDropdown, ModelRef } from '../../shared/model-dropdown';
import { PersistSizeDirective } from '../../shared/persist-size.directive';
import { ClickOutsideDirective } from '../../shared/click-outside.directive';
import { Icon } from '../../shared/icon';
import { ProviderIcon } from '../../shared/provider-icon';
import { RunDetail } from '../../shared/run-detail';
import { TextField } from '../../shared/text-field';
import { formatTimeSpan, formatCost, timeSpanToMs, formatBytes } from '../../shared/format';
import { MIN_PANEL_WIDTH, MAX_PANEL_WIDTH, clamp, loadPanelWidth, savePanelWidth } from '../../shared/panel-width';

type Tab = 'current' | 'history' | 'compare' | 'models';

interface MultiModelRunRow {
  providerId: string;
  modelId: string;
  status: 'running' | 'done' | 'canceled' | 'error';
  run: ExecutionRun | null;
  streamingOutput: string;
  handle: CancelableExecution<ExecutionRun> | null;
}

@Component({
  selector: 'app-workspace-shell',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ModelDropdown, PersistSizeDirective, ClickOutsideDirective, Icon, ProviderIcon, RunDetail, TextField],
  templateUrl: './workspace-shell.html',
})
export class WorkspaceShell implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly ExecutionStatusLabel = ExecutionStatusLabel;
  readonly formatTimeSpan = formatTimeSpan;
  readonly userContextHint = '— supports {{workspace.var}}, {{RequestName.output}}, {{RequestName.json.path}}';
  readonly formatCost = formatCost;

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

  activeTab = signal<Tab>('current');
  readonly tabs: Tab[] = ['current', 'history', 'compare', 'models'];
  executing = signal(false);
  streamingOutput = signal('');
  currentRun = signal<ExecutionRun | null>(null);
  private activeExecution: CancelableExecution<ExecutionRun> | null = null;

  compareModelsOpen = signal(false);
  compareModelsSelection = signal<ModelRef[]>([]);
  multiModelRuns = signal<MultiModelRunRow[]>([]);
  multiModelRunning = computed(() => this.multiModelRuns().some(r => r.status === 'running'));
  selectedMultiModelIndex = signal<number | null>(null);

  runs = signal<ExecutionRun[]>([]);
  selectedHistoryRun = signal<ExecutionRun | null>(null);
  showArchivedRuns = signal(false);
  visibleHistoryRuns = computed(() => this.runs().filter(r => this.showArchivedRuns() || !r.isArchived));
  hiddenArchivedCount = computed(() => this.runs().filter(r => r.isArchived).length);

  /** Compare several runs of the SAME request (e.g. different models/attempts at one task) —
   * more useful than the Compare tab's "latest run of each different request" comparison. Also
   * doubles as the selection for bulk archive/unarchive. */
  historyCompareSelection = signal<Set<string>>(new Set());
  historyCompareMode = signal(false);
  allVisibleHistoryRunsSelected = computed(() => {
    const visible = this.visibleHistoryRuns();
    return visible.length > 0 && visible.every(r => this.historyCompareSelection().has(r.id));
  });
  /** Whether every currently-selected run is already archived — flips the bulk action between "Archive" and "Unarchive". */
  selectedHistoryRunsAllArchived = computed(() => {
    const ids = this.historyCompareSelection();
    const selected = this.runs().filter(r => ids.has(r.id));
    return selected.length > 0 && selected.every(r => r.isArchived);
  });
  historyComparisonRuns = computed(() => this.runs().filter(r => this.historyCompareSelection().has(r.id)));

  benchmarkCount = signal(5);
  benchmarking = signal(false);
  benchmarkResult = signal<BenchmarkResponse | null>(null);

  compareSelection = signal<Set<string>>(new Set());
  comparisonRows = signal<ComparisonRow[]>([]);
  selectedComparisonRequestId = signal<string | null>(null);
  selectedComparisonRunId = signal<string | null>(null);
  selectedComparisonRun = signal<ExecutionRun | null>(null);
  loadingComparisonRun = signal(false);

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
    this.historyCompareSelection.set(new Set());
    this.historyCompareMode.set(false);
    this.benchmarkResult.set(null);
    this.activeTab.set('current');
    this.applyRuns(await this.api.listRuns(request.id));
  }

  /**
   * Sets the run list and keeps the History tab's selection valid — defaults to the latest
   * non-archived run (already ordered by StartedAt desc server-side) when nothing is selected or
   * the previously selected run fell out of the list, but otherwise preserves the user's manual
   * selection across unrelated refreshes (e.g. a benchmark run while viewing an older run).
   */
  private applyRuns(list: ExecutionRun[]) {
    this.runs.set(list);
    const current = this.selectedHistoryRun();
    if (!current || !list.some(r => r.id === current.id)) {
      this.selectedHistoryRun.set(list.find(r => !r.isArchived) ?? list[0] ?? null);
    }
  }

  toggleShowArchivedRuns() {
    this.showArchivedRuns.set(!this.showArchivedRuns());
  }

  async setRunArchived(run: ExecutionRun, isArchived: boolean) {
    const updated = await this.api.archiveRun(run.id, isArchived);
    this.runs.set(this.runs().map(r => (r.id === updated.id ? updated : r)));
    // If the archived run was selected and is now hidden, fall back to the next visible run so
    // the detail panel doesn't keep showing a row that just disappeared from the list.
    if (this.selectedHistoryRun()?.id === updated.id && isArchived && !this.showArchivedRuns()) {
      this.selectedHistoryRun.set(this.visibleHistoryRuns()[0] ?? null);
    } else if (this.selectedHistoryRun()?.id === updated.id) {
      this.selectedHistoryRun.set(updated);
    }
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

  // --- Drag-and-drop reordering of the task list. Reorders the list live as you drag over each
  // row (so the list always shows exactly what dropping now would produce), then persists the
  // whole new order once the drag ends — this is also the order Execution Plans list tasks in. ---
  draggingRequestId = signal<string | null>(null);

  onRequestReorderDragStart(event: DragEvent, request: AiRequestDefinition) {
    this.draggingRequestId.set(request.id);
    event.dataTransfer?.setData('text/plain', request.id);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  onRequestReorderDragOver(event: DragEvent, overRequest: AiRequestDefinition) {
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    const draggingId = this.draggingRequestId();
    if (!draggingId || draggingId === overRequest.id) return;

    const list = [...this.requests()];
    const fromIndex = list.findIndex(r => r.id === draggingId);
    const toIndex = list.findIndex(r => r.id === overRequest.id);
    if (fromIndex === -1 || toIndex === -1) return;
    const [moved] = list.splice(fromIndex, 1);
    list.splice(toIndex, 0, moved);
    this.requests.set(list);
  }

  async onRequestReorderDragEnd() {
    const draggingId = this.draggingRequestId();
    this.draggingRequestId.set(null);
    if (!draggingId) return;
    await this.api.reorderRequests(this.workspaceId(), this.requests().map(r => r.id));
  }

  async execute() {
    const selected = this.selectedRequest();
    if (!selected || this.dirty()) return;
    this.executing.set(true);
    this.streamingOutput.set('');
    this.currentRun.set(null);
    this.activeTab.set('current');
    this.startTimer();

    try {
      const handle = selected.streamingEnabled
        ? this.api.executeRequestStream(selected.id, (evt: AiStreamEvent) => {
            if (evt.kind === 2 && evt.textDelta) {
              this.streamingOutput.set(this.streamingOutput() + evt.textDelta);
            }
          })
        : this.api.executeRequest(selected.id);
      this.activeExecution = handle;
      const run = await handle.promise;
      if (run) this.currentRun.set(run);
      // A canceled run (or none) is still fetched via history below, since the server persists
      // it even when the client-side promise resolves to null.
      this.applyRuns(await this.api.listRuns(selected.id));
      this.observedStats.set(await this.api.observedModelStats());
    } finally {
      this.activeExecution = null;
      this.executing.set(false);
      this.stopTimer();
    }
  }

  stopExecution() {
    this.activeExecution?.cancel();
  }

  /** Runs the current request against each selected model in parallel, so results can be compared side by side. */
  async runOnSelectedModels() {
    const selected = this.selectedRequest();
    const targets = this.compareModelsSelection();
    if (!selected || this.dirty() || targets.length === 0) return;

    this.activeTab.set('models');
    this.multiModelRuns.set(targets.map(t => ({
      providerId: t.providerId,
      modelId: t.modelId,
      status: 'running',
      run: null,
      streamingOutput: '',
      handle: null,
    })));
    this.selectedMultiModelIndex.set(0);

    const patchRow = (i: number, patch: Partial<MultiModelRunRow>) => {
      this.multiModelRuns.set(this.multiModelRuns().map((r, idx) => (idx === i ? { ...r, ...patch } : r)));
    };

    const tasks = targets.map(async (t, i) => {
      try {
        const handle = selected.streamingEnabled
          ? this.api.executeRequestStream(selected.id, (evt: AiStreamEvent) => {
              if (evt.kind === 2 && evt.textDelta) {
                const row = this.multiModelRuns()[i];
                patchRow(i, { streamingOutput: (row?.streamingOutput ?? '') + evt.textDelta });
              }
            }, t)
          : this.api.executeRequest(selected.id, t);
        patchRow(i, { handle });
        const run = await handle.promise;
        patchRow(i, { run, status: run ? 'done' : 'canceled' });
      } catch {
        patchRow(i, { status: 'error' });
      }
    });

    await Promise.all(tasks);
    this.applyRuns(await this.api.listRuns(selected.id));
    this.observedStats.set(await this.api.observedModelStats());
  }

  selectMultiModelRow(i: number) {
    this.selectedMultiModelIndex.set(i);
  }

  stopMultiModelRun(i: number) {
    this.multiModelRuns()[i]?.handle?.cancel();
  }

  stopAllMultiModelRuns() {
    for (const row of this.multiModelRuns()) row.handle?.cancel();
  }

  async runBenchmark() {
    const selected = this.selectedRequest();
    if (!selected) return;
    this.benchmarking.set(true);
    this.startTimer();
    try {
      const result = await this.api.benchmark(selected.id, this.benchmarkCount());
      this.benchmarkResult.set(result);
      this.applyRuns(await this.api.listRuns(selected.id));
    } finally {
      this.benchmarking.set(false);
      this.stopTimer();
    }
  }

  viewHistoryRun(run: ExecutionRun) {
    this.selectedHistoryRun.set(run);
    this.historyCompareMode.set(false);
  }

  toggleHistoryCompareSelection(id: string) {
    const set = new Set(this.historyCompareSelection());
    if (set.has(id)) set.delete(id);
    else set.add(id);
    this.historyCompareSelection.set(set);
  }

  toggleSelectAllHistoryRuns() {
    this.historyCompareSelection.set(
      this.allVisibleHistoryRunsSelected() ? new Set() : new Set(this.visibleHistoryRuns().map(r => r.id)),
    );
  }

  async archiveOrUnarchiveSelectedHistoryRuns() {
    const ids = this.historyCompareSelection();
    if (ids.size === 0) return;
    const targetArchivedValue = !this.selectedHistoryRunsAllArchived();
    const targets = this.runs().filter(r => ids.has(r.id) && r.isArchived !== targetArchivedValue);
    await Promise.all(targets.map(r => this.setRunArchived(r, targetArchivedValue)));
    this.historyCompareSelection.set(new Set());
  }

  startHistoryComparison() {
    if (this.historyCompareSelection().size < 2) return;
    this.historyCompareMode.set(true);
  }

  exitHistoryComparison() {
    this.historyCompareMode.set(false);
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
    const rows = await this.api.comparison(ids);
    this.comparisonRows.set(rows);
    this.activeTab.set('compare');
    const withRun = rows.find(r => r.runId);
    if (withRun) {
      await this.selectComparisonRow(withRun);
    } else {
      this.selectedComparisonRequestId.set(null);
      this.selectedComparisonRunId.set(null);
      this.selectedComparisonRun.set(null);
    }
  }

  /** Compare rows only carry summary metrics — fetch the full run so the shared run-detail view can render it. */
  async selectComparisonRow(row: ComparisonRow) {
    this.selectedComparisonRequestId.set(row.requestId);
    this.selectedComparisonRunId.set(row.runId);
    if (!row.runId) {
      this.selectedComparisonRun.set(null);
      return;
    }
    this.loadingComparisonRun.set(true);
    try {
      const run = await this.api.getRun(row.runId);
      // Ignore a stale response if the user clicked another row before this fetch resolved.
      if (this.selectedComparisonRunId() === row.runId) {
        this.selectedComparisonRun.set(run);
      }
    } finally {
      if (this.selectedComparisonRunId() === row.runId) {
        this.loadingComparisonRun.set(false);
      }
    }
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

  statusLabel(status: ExecutionStatus): string {
    return ExecutionStatusLabel[status];
  }

  tabIcon(tab: Tab): string {
    switch (tab) {
      case 'current': return 'eye';
      case 'history': return 'clock';
      case 'compare': return 'scale';
      case 'models': return 'copy';
    }
  }

  tabLabel(tab: Tab): string {
    switch (tab) {
      case 'current': return 'Current';
      case 'history': return 'History';
      case 'compare': return 'Compare';
      case 'models': return 'Multi-Model';
    }
  }

  /**
   * The reasoning effort a run against this model will actually use, shown before execution so
   * the Multi-Model grid isn't blank until each row completes. Mirrors the server-side default in
   * AiRequestExecutor.DefaultReasoningEffort — the request's own explicit choice if set, else the
   * model's highest supported level, else null for a non-reasoning/unrecognized model.
   */
  predictedReasoningEffort(providerId: string, modelId: string): string | null {
    const explicit = this.draft()?.reasoningEffort;
    if (explicit) return explicit;

    const model = this.models().find(m => m.providerId === providerId && m.modelId === modelId);
    if (!model?.supportsReasoning) return null;

    const levels = model.supportedReasoningLevels;
    return levels.includes('high') ? 'high' : (levels[levels.length - 1] ?? null);
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

