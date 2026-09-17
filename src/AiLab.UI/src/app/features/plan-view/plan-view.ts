import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import * as dagre from 'dagre';
import { ApiService } from '../../core/api.service';
import {
  ExecutionPlan, AiRequestDefinition, ExecutionPlanRun, PlanExecutionEvent, ExecutionRun,
  ExecutionLevel, ExecutionPlanValidationResult, Attachment,
} from '../../core/models';
import { formatTimeSpan, formatCost, formatBytes } from '../../shared/format';
import { Icon } from '../../shared/icon';
import { RunDetail } from '../../shared/run-detail';
import { TextField } from '../../shared/text-field';
import { ClickOutsideDirective } from '../../shared/click-outside.directive';
import { MIN_PANEL_WIDTH, MAX_PANEL_WIDTH, clamp, loadPanelWidth, savePanelWidth } from '../../shared/panel-width';

interface GraphNode {
  id: string;
  request: AiRequestDefinition;
  x: number;
  y: number;
  width: number;
  height: number;
  isFinalOutput: boolean;
  latestRun: ExecutionRun | null;
  status: 'idle' | 'running' | 'completed' | 'failed' | 'canceled';
}

interface GraphEdge {
  from: string;
  to: string;
  points: { x: number; y: number }[];
}

const NODE_WIDTH = 200;
const NODE_HEIGHT = 76;

@Component({
  selector: 'app-plan-view',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, Icon, RunDetail, TextField, ClickOutsideDirective],
  templateUrl: './plan-view.html',
})
export class PlanView implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly formatTimeSpan = formatTimeSpan;
  readonly formatCost = formatCost;
  readonly userContextHint = '— supports {{workspace.var}}, {{RequestName.output}}, {{RequestName.json.path}}';

  workspaceId = signal('');
  planId = signal('');
  plan = signal<ExecutionPlan | null>(null);
  levels = signal<ExecutionLevel[]>([]);
  validation = signal<ExecutionPlanValidationResult | null>(null);
  allRequests = signal<AiRequestDefinition[]>([]);
  requestsById = computed(() => new Map(this.allRequests().map(r => [r.id, r])));
  attachments = signal<Attachment[]>([]);
  attachmentsById = computed(() => new Map(this.attachments().map(a => [a.id, a])));

  editName = signal('');
  editRequestIds = signal<Set<string>>(new Set());
  editFinalOutputId = signal<string | null>(null);
  editDependencies = signal<{ from: string; to: string }[]>([]);
  addEdgeFrom = signal<string>('');
  addEdgeTo = signal<string>('');
  addEdgeError = signal<string | null>(null);
  saveError = signal<string[] | null>(null);
  saving = signal(false);

  editRequests = computed(() => this.allRequests().filter(r => this.editRequestIds().has(r.id)));

  // --- Inline dependency-row editing ---
  editingEdgeIndex = signal<number | null>(null);
  editingEdgeDraft = signal<{ from: string; to: string }>({ from: '', to: '' });
  editingEdgeError = signal<string | null>(null);
  pendingDiscardEdgeIndex = signal<number | null>(null);
  highlightedEdgeIndex = signal<number | null>(null);

  activeDetailTab = signal<'task' | 'stats'>('task');
  activeTaskSubTab = signal<'info' | 'input' | 'result'>('info');

  isDirty = computed(() => {
    const plan = this.plan();
    if (!plan) return false;
    if (this.editingEdgeIndex() !== null) return true;
    if (this.editName().trim() !== plan.name) return true;

    const savedIds = new Set(plan.requests.map(r => r.aiRequestId));
    const editIds = this.editRequestIds();
    if (savedIds.size !== editIds.size || [...savedIds].some(id => !editIds.has(id))) return true;

    const savedFinal = plan.requests.find(r => r.isFinalOutput)?.aiRequestId ?? null;
    if (savedFinal !== this.editFinalOutputId()) return true;

    const savedDeps = new Set(plan.dependencies.map(d => `${d.fromRequestId}>${d.toRequestId}`));
    const editDeps = new Set(this.editDependencies().map(d => `${d.from}>${d.to}`));
    if (savedDeps.size !== editDeps.size || [...savedDeps].some(k => !editDeps.has(k))) return true;

    return false;
  });

  // --- Edit sidebar width (px), resizable by dragging, persisted across reloads ---
  sidebarWidth = signal(loadPanelWidth('plan-sidebar', 320));
  private resizing = false;
  private resizeStartX = 0;
  private resizeStartWidth = 0;
  private readonly onResizeMove = (e: MouseEvent) => this.handleResizeMove(e);
  private readonly onResizeUp = () => this.stopResize();

  executing = signal(false);
  nodeStatuses = signal<Map<string, GraphNode['status']>>(new Map());
  latestRuns = signal<Map<string, ExecutionRun>>(new Map());
  planRuns = signal<ExecutionPlanRun[]>([]);
  currentPlanRun = signal<ExecutionPlanRun | null>(null);
  loadingPlanRun = signal(false);

  selectedNodeId = signal<string | null>(null);
  selectedNode = computed(() => this.nodes().find(n => n.id === this.selectedNodeId()) ?? null);

  // Nodes/edges are drawn from the live edit state (editRequestIds/editDependencies/editFinalOutputId)
  // rather than the last-saved `plan()` — resetEditState() seeds that state from the saved plan on
  // load/save/cancel, so the graph always reflects what's on screen, including uncommitted edits.
  nodes = computed<GraphNode[]>(() => {
    const requestIds = this.editRequestIds();
    if (requestIds.size === 0) return [];

    const g = new dagre.graphlib.Graph();
    g.setGraph({ rankdir: 'TB', nodesep: 30, ranksep: 60 });
    g.setDefaultEdgeLabel(() => ({}));

    for (const id of requestIds) {
      g.setNode(id, { width: NODE_WIDTH, height: NODE_HEIGHT });
    }
    for (const d of this.editDependencies()) {
      if (requestIds.has(d.from) && requestIds.has(d.to)) g.setEdge(d.from, d.to);
    }

    dagre.layout(g);

    const statuses = this.nodeStatuses();
    const runs = this.latestRuns();
    const requestsById = this.requestsById();
    const finalOutputId = this.editFinalOutputId();

    const result: GraphNode[] = [];
    for (const id of requestIds) {
      const n = g.node(id);
      const request = requestsById.get(id);
      if (!request || !n) continue;
      result.push({
        id,
        request,
        x: n.x - NODE_WIDTH / 2,
        y: n.y - NODE_HEIGHT / 2,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        isFinalOutput: finalOutputId === id,
        latestRun: runs.get(id) ?? null,
        status: statuses.get(id) ?? 'idle',
      });
    }

    return result;
  });

  graphSize = computed(() => {
    const nodes = this.nodes();
    const maxX = nodes.reduce((m, n) => Math.max(m, n.x + n.width), 0);
    const maxY = nodes.reduce((m, n) => Math.max(m, n.y + n.height), 0);
    return { width: maxX + 40 || 800, height: maxY + 40 || 400 };
  });

  edges = computed<GraphEdge[]>(() => {
    const nodesById = new Map(this.nodes().map(n => [n.id, n]));

    return this.editDependencies()
      .map(d => {
        const from = nodesById.get(d.from);
        const to = nodesById.get(d.to);
        if (!from || !to) return null;
        return {
          from: d.from,
          to: d.to,
          points: [
            { x: from.x + from.width / 2, y: from.y + from.height },
            { x: to.x + to.width / 2, y: to.y },
          ],
        };
      })
      .filter((e): e is GraphEdge => e !== null);
  });

  finalOutputRun = computed(() => {
    const finalId = this.editFinalOutputId();
    if (!finalId) return null;
    return this.latestRuns().get(finalId) ?? null;
  });

  async ngOnInit() {
    this.workspaceId.set(this.route.snapshot.paramMap.get('workspaceId')!);
    this.planId.set(this.route.snapshot.paramMap.get('planId')!);
    await this.loadAll();
  }

  ngOnDestroy() {
    window.removeEventListener('mousemove', this.onResizeMove);
    window.removeEventListener('mouseup', this.onResizeUp);
  }

  startResize(event: MouseEvent) {
    this.resizing = true;
    this.resizeStartX = event.clientX;
    this.resizeStartWidth = this.sidebarWidth();
    window.addEventListener('mousemove', this.onResizeMove);
    window.addEventListener('mouseup', this.onResizeUp);
    event.preventDefault();
  }

  private handleResizeMove(e: MouseEvent) {
    if (!this.resizing) return;
    const delta = e.clientX - this.resizeStartX;
    this.sidebarWidth.set(clamp(this.resizeStartWidth + delta, MIN_PANEL_WIDTH, MAX_PANEL_WIDTH));
  }

  private stopResize() {
    if (this.resizing) savePanelWidth('plan-sidebar', this.sidebarWidth());
    this.resizing = false;
    window.removeEventListener('mousemove', this.onResizeMove);
    window.removeEventListener('mouseup', this.onResizeUp);
  }

  async loadAll() {
    const [detail, requests, runs, attachments] = await Promise.all([
      this.api.getExecutionPlan(this.planId()),
      this.api.listRequests(this.workspaceId()),
      this.api.listPlanRuns(this.planId()),
      this.api.listAttachments(this.workspaceId()),
    ]);
    this.plan.set(detail.plan);
    this.levels.set(detail.levels);
    this.validation.set(detail.validation);
    this.allRequests.set(requests);
    this.planRuns.set(runs);
    this.attachments.set(attachments);
    this.resetEditState();

    if (runs.length > 0) {
      await this.applyPlanRunResult(runs[0]);
    }
  }

  resetEditState() {
    const plan = this.plan();
    if (!plan) return;
    this.editName.set(plan.name);
    this.editRequestIds.set(new Set(plan.requests.map(r => r.aiRequestId)));
    this.editFinalOutputId.set(plan.requests.find(r => r.isFinalOutput)?.aiRequestId ?? null);
    this.editDependencies.set(plan.dependencies.map(d => ({ from: d.fromRequestId, to: d.toRequestId })));
    this.addEdgeFrom.set('');
    this.addEdgeTo.set('');
    this.addEdgeError.set(null);
    this.editingEdgeIndex.set(null);
    this.editingEdgeError.set(null);
    this.pendingDiscardEdgeIndex.set(null);
    this.highlightedEdgeIndex.set(null);
  }

  cancelEdits() {
    this.resetEditState();
    this.saveError.set(null);
    this.addEdgeError.set(null);
  }

  toggleEditRequest(id: string) {
    const set = new Set(this.editRequestIds());
    if (set.has(id)) set.delete(id);
    else set.add(id);
    this.editRequestIds.set(set);

    if (!set.has(id)) {
      // Dropping a request from the plan would leave any edge touching it dangling
      // (referencing a request the plan no longer includes) — the API rejects that.
      this.editDependencies.set(this.editDependencies().filter(d => d.from !== id && d.to !== id));
      if (this.editFinalOutputId() === id) this.editFinalOutputId.set(null);
      this.resetEdgeEditState();
    }
  }

  addEdge() {
    const from = this.addEdgeFrom();
    const to = this.addEdgeTo();
    this.addEdgeError.set(null);
    if (!from || !to) return;
    if (from === to) {
      this.addEdgeError.set('A request cannot depend on itself.');
      return;
    }
    if (this.editDependencies().some(d => d.from === from && d.to === to)) return;

    // Catch cycles of any length (A→B→C→A, not just direct A→B→A) before they ever reach the
    // API — walk the existing edges from `to` looking for a path back to `from`; if one exists,
    // adding from→to would close it into a cycle.
    const path = this.findPath(this.editDependencies(), to, from);
    if (path) {
      const cycleNames = [from, ...path].map(id => this.requestsById().get(id)?.name ?? id);
      this.addEdgeError.set(`This would create a cycle: ${cycleNames.join(' → ')}.`);
      return;
    }

    this.editDependencies.set([...this.editDependencies(), { from, to }]);
    this.addEdgeFrom.set('');
    this.addEdgeTo.set('');
  }

  removeEdge(index: number) {
    this.editDependencies.set(this.editDependencies().filter((_, i) => i !== index));
    this.resetEdgeEditState();
  }

  /** Clears any in-progress inline edge edit/highlight — used whenever the dependency list is
   * restructured out from under it (removing a row, unchecking a request) so stale indices can't
   * point at the wrong row. */
  private resetEdgeEditState() {
    this.editingEdgeIndex.set(null);
    this.editingEdgeError.set(null);
    this.pendingDiscardEdgeIndex.set(null);
    this.highlightedEdgeIndex.set(null);
  }

  startEditEdge(index: number) {
    if (this.editingEdgeIndex() !== null && this.editingEdgeIndex() !== index) {
      if (!this.leaveEdgeEdit()) return; // blocked: a discard-confirmation is now showing on that row
    }
    const dep = this.editDependencies()[index];
    if (!dep) return;
    this.editingEdgeIndex.set(index);
    this.editingEdgeDraft.set({ ...dep });
    this.editingEdgeError.set(null);
    this.pendingDiscardEdgeIndex.set(null);
    this.highlightedEdgeIndex.set(null);
  }

  cancelEdgeEdit() {
    this.leaveEdgeEdit();
  }

  /** A click landed outside dependency row `i` — if that row is the one being edited, this is
   * the "click away" case: close it (silently if unchanged, via a discard-confirmation if not). */
  onEdgeRowClickOutside(i: number) {
    if (this.editingEdgeIndex() === i) this.cancelEdgeEdit();
  }

  /** Attempts to exit the in-progress inline edge edit. Returns true once it has exited (there
   * were no changes, or the row now shows "no changes" trivially); if the draft actually differs
   * from the saved edge it instead surfaces a discard-confirmation on that row and returns false. */
  private leaveEdgeEdit(): boolean {
    const idx = this.editingEdgeIndex();
    if (idx === null) return true;
    const original = this.editDependencies()[idx];
    const draft = this.editingEdgeDraft();
    const changed = !original || original.from !== draft.from || original.to !== draft.to;
    if (!changed) {
      this.editingEdgeIndex.set(null);
      this.editingEdgeError.set(null);
      return true;
    }
    this.pendingDiscardEdgeIndex.set(idx);
    return false;
  }

  confirmDiscardEdgeEdit() {
    this.editingEdgeIndex.set(null);
    this.editingEdgeError.set(null);
    this.pendingDiscardEdgeIndex.set(null);
  }

  keepEditingEdge() {
    this.pendingDiscardEdgeIndex.set(null);
  }

  saveEdgeEdit() {
    const idx = this.editingEdgeIndex();
    if (idx === null) return;
    const draft = this.editingEdgeDraft();
    this.editingEdgeError.set(null);

    if (!draft.from || !draft.to) {
      this.editingEdgeError.set('Select both a from and to request.');
      return;
    }
    if (draft.from === draft.to) {
      this.editingEdgeError.set('A request cannot depend on itself.');
      return;
    }
    const others = this.editDependencies().filter((_, i) => i !== idx);
    if (others.some(d => d.from === draft.from && d.to === draft.to)) {
      this.editingEdgeError.set('This dependency already exists.');
      return;
    }
    const path = this.findPath(others, draft.to, draft.from);
    if (path) {
      const cycleNames = [draft.from, ...path].map(id => this.requestsById().get(id)?.name ?? id);
      this.editingEdgeError.set(`This would create a cycle: ${cycleNames.join(' → ')}.`);
      return;
    }

    const updated = [...this.editDependencies()];
    updated[idx] = { ...draft };
    this.editDependencies.set(updated);
    this.editingEdgeIndex.set(null);
    this.pendingDiscardEdgeIndex.set(null);
  }

  /** Clicking an edge in the graph highlights its row in the sidebar so the user knows what
   * they'd be editing — it doesn't enter edit mode itself. */
  highlightEdge(edge: GraphEdge) {
    const idx = this.editDependencies().findIndex(d => d.from === edge.from && d.to === edge.to);
    this.highlightedEdgeIndex.set(idx >= 0 ? idx : null);
  }

  /** BFS for a path from `start` to `target` following existing dependency edges; null if none. */
  private findPath(dependencies: { from: string; to: string }[], start: string, target: string): string[] | null {
    const adjacency = new Map<string, string[]>();
    for (const d of dependencies) {
      if (!adjacency.has(d.from)) adjacency.set(d.from, []);
      adjacency.get(d.from)!.push(d.to);
    }

    const parent = new Map<string, string>();
    const visited = new Set<string>([start]);
    const queue = [start];
    while (queue.length > 0) {
      const node = queue.shift()!;
      if (node === target) {
        const path = [target];
        let cur = target;
        while (cur !== start) {
          cur = parent.get(cur)!;
          path.unshift(cur);
        }
        return path;
      }
      for (const next of adjacency.get(node) ?? []) {
        if (!visited.has(next)) {
          visited.add(next);
          parent.set(next, node);
          queue.push(next);
        }
      }
    }
    return null;
  }

  async saveEdits() {
    const plan = this.plan();
    if (!plan) return;
    const requests = Array.from(this.editRequestIds()).map(id => ({
      aiRequestId: id,
      isFinalOutput: id === this.editFinalOutputId(),
    }));

    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.api.updateExecutionPlan(plan.id, this.editName().trim() || plan.name, requests, this.editDependencies());
      await this.loadAll();
    } catch (err: any) {
      // Keep the edit panel open with the user's in-progress edits intact — a failed save here
      // (e.g. a validation error from the plan graph) must never silently discard their changes.
      this.saveError.set(err?.error?.errors ?? ['Failed to save the plan. Please try again.']);
    } finally {
      this.saving.set(false);
    }
  }

  async execute() {
    const plan = this.plan();
    if (!plan) return;
    this.executing.set(true);
    this.nodeStatuses.set(new Map());
    this.latestRuns.set(new Map());
    this.currentPlanRun.set(null);

    try {
      const planRun = await this.api.executeExecutionPlanStream(plan.id, (evt: PlanExecutionEvent) => {
        if (evt.kind === 0 && evt.aiRequestId) {
          const statuses = new Map(this.nodeStatuses());
          statuses.set(evt.aiRequestId, 'running');
          this.nodeStatuses.set(statuses);
        } else if (evt.kind === 1 && evt.aiRequestId && evt.run) {
          const statuses = new Map(this.nodeStatuses());
          statuses.set(evt.aiRequestId, evt.run.status === 3 ? 'completed' : evt.run.status === 5 ? 'canceled' : 'failed');
          this.nodeStatuses.set(statuses);

          const runs = new Map(this.latestRuns());
          runs.set(evt.aiRequestId, evt.run);
          this.latestRuns.set(runs);
        }
      });
      this.currentPlanRun.set(planRun);
      this.planRuns.set(await this.api.listPlanRuns(plan.id));
    } finally {
      this.executing.set(false);
    }
  }

  async viewPlanRun(run: ExecutionPlanRun) {
    if (this.executing()) return;
    await this.applyPlanRunResult(run);
  }

  private async applyPlanRunResult(run: ExecutionPlanRun) {
    this.currentPlanRun.set(run);
    this.loadingPlanRun.set(true);
    try {
      // Plan runs only store `executionRunIds` (not the ExecutionRun objects themselves, to keep
      // the plan-run payload small) — fetch each one and key by aiRequestId so nodes/detail panel
      // can look theirs up the same way the live execute() stream does via `latestRuns`.
      const runs = await Promise.all(run.executionRunIds.map(id => this.api.getRun(id)));
      const runsMap = new Map(runs.map(r => [r.aiRequestId, r]));
      const statuses = new Map<string, GraphNode['status']>();
      for (const r of runs) {
        statuses.set(r.aiRequestId, r.status === 3 ? 'completed' : r.status === 5 ? 'canceled' : 'failed');
      }
      this.latestRuns.set(runsMap);
      this.nodeStatuses.set(statuses);
    } finally {
      this.loadingPlanRun.set(false);
    }
  }

  selectNode(id: string) {
    const next = this.selectedNodeId() === id ? null : id;
    this.selectedNodeId.set(next);
    if (next) {
      this.activeDetailTab.set('task');
      this.activeTaskSubTab.set('info');
    }
  }

  bindingLabel(key: string): string {
    return `{{${key}}}`;
  }

  attachmentLabelsFor(ids: string[]): string[] {
    return ids.map(id => {
      const a = this.attachmentsById().get(id);
      return a ? `${a.filename} (${formatBytes(a.sizeBytes)})` : id;
    });
  }

  nodeStatusColor(status: GraphNode['status']): string {
    switch (status) {
      case 'running': return '#3b82f6';
      case 'completed': return '#22c55e';
      case 'failed': return '#ef4444';
      case 'canceled': return '#a3a3a3';
      default: return '#d4d4d4';
    }
  }
}
