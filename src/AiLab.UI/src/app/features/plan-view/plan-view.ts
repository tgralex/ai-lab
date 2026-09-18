import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import * as dagre from 'dagre';
import { ApiService } from '../../core/api.service';
import {
  ExecutionPlan, AiRequestDefinition, ExecutionPlanRun, PlanExecutionEvent, ExecutionRun,
  ExecutionLevel, ExecutionPlanValidationResult, Attachment, Workspace,
} from '../../core/models';
import { formatTimeSpan, formatCost, formatBytes, timeSpanToMs } from '../../shared/format';
import { Icon } from '../../shared/icon';
import { RunDetail } from '../../shared/run-detail';
import { TextField } from '../../shared/text-field';
import { ClickOutsideDirective } from '../../shared/click-outside.directive';
import { AutofocusSelectDirective } from '../../shared/autofocus-select.directive';
import { MIN_PANEL_WIDTH, MAX_PANEL_WIDTH, clamp, loadPanelWidth, savePanelWidth } from '../../shared/panel-width';

/** A node being edited in the sidebar — the same AiRequestDefinition can appear as more than one
 * of these (each with its own `id`), so `id` (not `aiRequestId`) is what identifies a node
 * throughout the graph, dependencies, SSE events, and run history. */
interface EditNode {
  id: string;
  aiRequestId: string;
  label: string | null;
  isFinalOutput: boolean;
  /** Manually-dragged canvas position; null means "auto-place via dagre". */
  x: number | null;
  y: number | null;
}

interface GraphNode {
  id: string;
  request: AiRequestDefinition;
  label: string;
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
  imports: [CommonModule, FormsModule, RouterLink, Icon, RunDetail, TextField, ClickOutsideDirective, AutofocusSelectDirective],
  templateUrl: './plan-view.html',
})
export class PlanView implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly formatTimeSpan = formatTimeSpan;
  readonly formatCost = formatCost;
  readonly userContextHint = '— supports {{workspace.var}}, {{RequestName.output}}, {{RequestName.json.path}}';

  workspaceId = signal('');
  workspace = signal<Workspace | null>(null);
  planId = signal('');
  plan = signal<ExecutionPlan | null>(null);
  levels = signal<ExecutionLevel[]>([]);
  validation = signal<ExecutionPlanValidationResult | null>(null);
  allRequests = signal<AiRequestDefinition[]>([]);
  requestsById = computed(() => new Map(this.allRequests().map(r => [r.id, r])));
  attachments = signal<Attachment[]>([]);
  attachmentsById = computed(() => new Map(this.attachments().map(a => [a.id, a])));

  editName = signal('');
  editNodes = signal<EditNode[]>([]);
  editNodesById = computed(() => new Map(this.editNodes().map(n => [n.id, n])));

  /** "Nodes in plan" list order: dependency-free nodes first, most-depended-on-chain last (same
   * depth metric as the backend's DeriveLevels — a node's level is one past its deepest
   * predecessor), then each node's underlying task's own drag-and-drop SortOrder, then label —
   * so the list roughly reads top-to-bottom the way the graph executes. */
  orderedEditNodes = computed<EditNode[]>(() => {
    const nodes = this.editNodes();
    const level = this.computeNodeLevels(nodes, this.editDependencies());
    const requestsById = this.requestsById();

    return [...nodes].sort((a, b) => {
      const levelDiff = (level.get(a.id) ?? 0) - (level.get(b.id) ?? 0);
      if (levelDiff !== 0) return levelDiff;
      const orderDiff = (requestsById.get(a.aiRequestId)?.sortOrder ?? 0) - (requestsById.get(b.aiRequestId)?.sortOrder ?? 0);
      if (orderDiff !== 0) return orderDiff;
      return this.effectiveLabel(a).localeCompare(this.effectiveLabel(b));
    });
  });

  private computeNodeLevels(nodes: EditNode[], deps: { from: string; to: string }[]): Map<string, number> {
    const ids = new Set(nodes.map(n => n.id));
    const inDegree = new Map(nodes.map(n => [n.id, 0]));
    const adjacency = new Map<string, string[]>(nodes.map(n => [n.id, []]));
    for (const d of deps) {
      if (!ids.has(d.from) || !ids.has(d.to)) continue;
      inDegree.set(d.to, (inDegree.get(d.to) ?? 0) + 1);
      adjacency.get(d.from)!.push(d.to);
    }

    const level = new Map<string, number>();
    const remaining = new Set(ids);
    let currentLevel = 0;
    while (remaining.size > 0) {
      const current = [...remaining].filter(id => (inDegree.get(id) ?? 0) === 0);
      if (current.length === 0) {
        // A cycle would leave nodes stuck with a nonzero in-degree forever — validation blocks
        // saving one, but mid-edit the graph can transiently have one, so bail out rather than
        // loop forever: dump whatever's left at the current level.
        for (const id of remaining) level.set(id, currentLevel);
        break;
      }
      for (const id of current) {
        level.set(id, currentLevel);
        remaining.delete(id);
        for (const next of adjacency.get(id) ?? []) {
          inDegree.set(next, (inDegree.get(next) ?? 0) - 1);
        }
      }
      currentLevel++;
    }
    return level;
  }
  editDependencies = signal<{ from: string; to: string }[]>([]);
  addEdgeFrom = signal<string>('');
  addEdgeTo = signal<string>('');
  addEdgeError = signal<string | null>(null);
  saveError = signal<string[] | null>(null);
  saving = signal(false);

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

    const savedNodes = new Set(plan.requests.map(r => `${r.id}|${r.aiRequestId}|${r.label ?? ''}|${r.isFinalOutput}|${r.positionX ?? ''}|${r.positionY ?? ''}`));
    const editNodesSet = new Set(this.editNodes().map(n => `${n.id}|${n.aiRequestId}|${n.label ?? ''}|${n.isFinalOutput}|${n.x ?? ''}|${n.y ?? ''}`));
    if (savedNodes.size !== editNodesSet.size || [...savedNodes].some(k => !editNodesSet.has(k))) return true;

    const savedDeps = new Set(plan.dependencies.map(d => `${d.fromNodeId}>${d.toNodeId}`));
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
  nodeStartTimes = signal<Map<string, number>>(new Map());
  nowTick = signal(Date.now());
  private tickHandle: ReturnType<typeof setInterval> | null = null;
  latestRuns = signal<Map<string, ExecutionRun>>(new Map());
  planRuns = signal<ExecutionPlanRun[]>([]);
  currentPlanRun = signal<ExecutionPlanRun | null>(null);
  loadingPlanRun = signal(false);

  selectedNodeId = signal<string | null>(null);
  selectedNode = computed(() => this.nodes().find(n => n.id === this.selectedNodeId()) ?? null);

  nodeById(id: string): GraphNode | null {
    return this.nodes().find(n => n.id === id) ?? null;
  }

  /** `request.Name`, or the node's custom Label when set — this is what bindings like
   * {{Get a Number (2).output}} address, and what the graph/dropdowns display, so two instances
   * of the same request are visually and referentially distinguishable. */
  effectiveLabel(node: EditNode): string {
    return node.label ?? this.requestsById().get(node.aiRequestId)?.name ?? node.id;
  }

  // Nodes/edges are drawn from the live edit state (editNodes/editDependencies) rather than the
  // last-saved `plan()` — resetEditState() seeds that state from the saved plan on load/save/cancel,
  // so the graph always reflects what's on screen, including uncommitted edits.
  nodes = computed<GraphNode[]>(() => {
    const editNodes = this.editNodes();
    if (editNodes.length === 0) return [];

    const g = new dagre.graphlib.Graph();
    g.setGraph({ rankdir: 'TB', nodesep: 30, ranksep: 60 });
    g.setDefaultEdgeLabel(() => ({}));

    for (const n of editNodes) {
      g.setNode(n.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
    }
    const nodeIds = new Set(editNodes.map(n => n.id));
    for (const d of this.editDependencies()) {
      if (nodeIds.has(d.from) && nodeIds.has(d.to)) g.setEdge(d.from, d.to);
    }

    dagre.layout(g);

    const statuses = this.nodeStatuses();
    const runs = this.latestRuns();
    const requestsById = this.requestsById();

    const result: GraphNode[] = [];
    for (const n of editNodes) {
      const gNode = g.node(n.id);
      const request = requestsById.get(n.aiRequestId);
      if (!request || !gNode) continue;
      // A node the user has dragged keeps its own position instead of dagre's — dagre still lays
      // it out (it needs every node placed to route edges sensibly around it), we just ignore that
      // result for this node. Undragged nodes fall through to the auto-layout position as before.
      const hasManualPosition = n.x != null && n.y != null;
      result.push({
        id: n.id,
        request,
        label: this.effectiveLabel(n),
        x: hasManualPosition ? n.x! : gNode.x - NODE_WIDTH / 2,
        y: hasManualPosition ? n.y! : gNode.y - NODE_HEIGHT / 2,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        isFinalOutput: n.isFinalOutput,
        latestRun: runs.get(n.id) ?? null,
        status: statuses.get(n.id) ?? 'idle',
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
    const finalNode = this.editNodes().find(n => n.isFinalOutput);
    if (!finalNode) return null;
    return this.latestRuns().get(finalNode.id) ?? null;
  });

  async ngOnInit() {
    this.workspaceId.set(this.route.snapshot.paramMap.get('workspaceId')!);
    this.planId.set(this.route.snapshot.paramMap.get('planId')!);
    await this.loadAll();
  }

  ngOnDestroy() {
    window.removeEventListener('mousemove', this.onResizeMove);
    window.removeEventListener('mouseup', this.onResizeUp);
    window.removeEventListener('mousemove', this.onNodeDragMove);
    window.removeEventListener('mouseup', this.onNodeDragUp);
    if (this.tickHandle) clearInterval(this.tickHandle);
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
    const [detail, requests, runs, attachments, workspace] = await Promise.all([
      this.api.getExecutionPlan(this.planId()),
      this.api.listRequests(this.workspaceId()),
      this.api.listPlanRuns(this.planId()),
      this.api.listAttachments(this.workspaceId()),
      this.api.getWorkspace(this.workspaceId()),
    ]);
    this.workspace.set(workspace);
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
    // Round-trip each node's saved `id` (not a fresh one) — the backend keeps identities stable
    // across saves only when the client sends back the id of every node it isn't deleting, which
    // is what keeps dependency edges, run-history attribution, and bindings pointed at the right node.
    this.editNodes.set(plan.requests.map(r => ({ id: r.id, aiRequestId: r.aiRequestId, label: r.label, isFinalOutput: r.isFinalOutput, x: r.positionX, y: r.positionY })));
    this.editDependencies.set(plan.dependencies.map(d => ({ from: d.fromNodeId, to: d.toNodeId })));
    this.addEdgeFrom.set('');
    this.addEdgeTo.set('');
    this.addEdgeError.set(null);
    this.editingEdgeIndex.set(null);
    this.editingEdgeError.set(null);
    this.pendingDiscardEdgeIndex.set(null);
    this.highlightedEdgeIndex.set(null);
    this.pendingRemoveNodeId.set(null);
    this.connectingFromNodeId.set(null);
    this.renamingNodeId.set(null);
    this.editingNodeId.set(null);
    this.editingNodesSnapshot = null;
  }

  cancelEdits() {
    this.resetEditState();
    this.saveError.set(null);
    this.addEdgeError.set(null);
  }

  /** Adds a new, independent node for this request — clicking repeatedly is how the same request
   * gets reused multiple times in one plan. A 2nd/3rd+ instance auto-suggests a disambiguating
   * label ("Name (2)", "Name (3)", ...); the first instance stays unlabeled (falls back to the
   * request's own Name), so single-instance plans and their bindings are unaffected. */
  addNodeForRequest(request: AiRequestDefinition) {
    if (this.executing()) return;
    const existingCount = this.editNodes().filter(n => n.aiRequestId === request.id).length;
    const label = existingCount === 0 ? null : `${request.name} (${existingCount + 1})`;
    const id = crypto.randomUUID();
    this.editNodes.set([...this.editNodes(), { id, aiRequestId: request.id, label, isFinalOutput: false, x: null, y: null }]);
  }

  /** Clears every node's manually-dragged position, handing layout back to dagre. */
  resetLayout() {
    if (this.executing()) return;
    this.editNodes.set(this.editNodes().map(n => ({ ...n, x: null, y: null })));
  }

  pendingRemoveNodeId = signal<string | null>(null);

  /** How many dependency edges touch this node — surfaced in the removal confirmation so removing
   * a node's hidden blast radius (silently dropping its edges too) is never a surprise. */
  dependencyCountFor(nodeId: string): number {
    return this.editDependencies().filter(d => d.from === nodeId || d.to === nodeId).length;
  }

  // --- "Nodes in plan" row label: shown as plain text, like a dependency row — click it to edit,
  // click away (or Escape) to cancel without saving, Enter or the checkmark to commit. Unlike
  // dependency-row editing this never shows a discard-confirmation: renaming a node is low-stakes
  // enough that a plain cancel is the expected behavior. The "final" radio is also reachable while
  // a row is in this state, so canceling restores label AND isFinalOutput (for every node — setting
  // final also un-sets whichever node had it before) to how they were when editing started, not
  // just the label draft; other fields (e.g. a dragged x/y) are left as their current live values.
  editingNodeId = signal<string | null>(null);
  editingNodeLabelDraft = signal('');
  private editingNodesSnapshot: Map<string, { label: string | null; isFinalOutput: boolean }> | null = null;

  startEditNodeLabel(n: EditNode) {
    if (this.executing()) return;
    this.editingNodeId.set(n.id);
    this.editingNodeLabelDraft.set(n.label ?? this.requestsById().get(n.aiRequestId)?.name ?? '');
    this.editingNodesSnapshot = new Map(this.editNodes().map(node => [node.id, { label: node.label, isFinalOutput: node.isFinalOutput }]));
    // Also selects the node on the canvas — clicking a task's label in the sidebar and clicking
    // its box on the canvas are the same "focus this node" action, so both highlight each other.
    if (this.selectedNodeId() !== n.id) {
      this.selectedNodeId.set(n.id);
      this.activeDetailTab.set('task');
    }
  }

  commitNodeLabelEdit() {
    const nodeId = this.editingNodeId();
    if (nodeId === null) return;
    this.setNodeLabel(nodeId, this.editingNodeLabelDraft());
    this.editingNodeId.set(null);
    this.editingNodesSnapshot = null;
  }

  cancelNodeLabelEdit() {
    const snapshot = this.editingNodesSnapshot;
    if (snapshot) {
      this.editNodes.set(this.editNodes().map(n => {
        const saved = snapshot.get(n.id);
        return saved ? { ...n, label: saved.label, isFinalOutput: saved.isFinalOutput } : n;
      }));
    }
    this.editingNodeId.set(null);
    this.editingNodesSnapshot = null;
  }

  onNodeRowClickOutside(nodeId: string) {
    if (this.editingNodeId() === nodeId) this.cancelNodeLabelEdit();
  }

  requestRemoveNode(nodeId: string) {
    if (this.executing()) return;
    this.pendingRemoveNodeId.set(nodeId);
  }

  cancelRemoveNode() {
    this.pendingRemoveNodeId.set(null);
  }

  confirmRemoveNode() {
    const nodeId = this.pendingRemoveNodeId();
    if (!nodeId) return;
    this.editNodes.set(this.editNodes().filter(n => n.id !== nodeId));
    // Dropping a node would leave any edge touching it dangling (referencing a node the plan no
    // longer includes) — the API rejects that, so its edges are removed right along with it.
    this.editDependencies.set(this.editDependencies().filter(d => d.from !== nodeId && d.to !== nodeId));
    if (this.selectedNodeId() === nodeId) this.selectedNodeId.set(null);
    this.resetEdgeEditState();
    this.pendingRemoveNodeId.set(null);
  }

  /** Drag a request from the sidebar list onto the canvas to add it as a node — an alternative to
   * clicking it, for the same addNodeForRequest() behavior. */
  onRequestDragStart(event: DragEvent, request: AiRequestDefinition) {
    if (this.executing()) return;
    event.dataTransfer?.setData('text/plain', request.id);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy';
  }

  onCanvasDragOver(event: DragEvent) {
    if (this.executing()) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  }

  onCanvasDrop(event: DragEvent) {
    event.preventDefault();
    if (this.executing()) return;
    const requestId = event.dataTransfer?.getData('text/plain');
    const request = requestId ? this.requestsById().get(requestId) : undefined;
    if (request) this.addNodeForRequest(request);
  }

  setNodeLabel(nodeId: string, label: string) {
    if (this.executing()) return;
    this.editNodes.set(this.editNodes().map(n => n.id === nodeId ? { ...n, label: label.trim() || null } : n));
  }

  setEditName(name: string) {
    if (this.executing()) return;
    this.editName.set(name);
  }

  setFinalOutputId(nodeId: string) {
    if (this.executing()) return;
    this.editNodes.set(this.editNodes().map(n => ({ ...n, isFinalOutput: n.id === nodeId })));
  }

  addEdge() {
    if (this.executing()) return;
    const from = this.addEdgeFrom();
    const to = this.addEdgeTo();
    this.addEdgeError.set(null);
    if (!from || !to) return;
    if (from === to) {
      this.addEdgeError.set('A node cannot depend on itself.');
      return;
    }
    if (this.editDependencies().some(d => d.from === from && d.to === to)) return;

    // Catch cycles of any length (A→B→C→A, not just direct A→B→A) before they ever reach the
    // API — walk the existing edges from `to` looking for a path back to `from`; if one exists,
    // adding from→to would close it into a cycle.
    const path = this.findPath(this.editDependencies(), to, from);
    if (path) {
      const cycleNames = [from, ...path].map(id => this.labelFor(id));
      this.addEdgeError.set(`This would create a cycle: ${cycleNames.join(' → ')}.`);
      return;
    }

    this.editDependencies.set([...this.editDependencies(), { from, to }]);
    this.addEdgeFrom.set('');
    this.addEdgeTo.set('');
  }

  removeEdge(index: number) {
    if (this.executing()) return;
    this.editDependencies.set(this.editDependencies().filter((_, i) => i !== index));
    this.resetEdgeEditState();
  }

  labelFor(nodeId: string): string {
    const node = this.editNodesById().get(nodeId);
    return node ? this.effectiveLabel(node) : nodeId;
  }

  /** Clears any in-progress inline edge edit/highlight — used whenever the dependency list is
   * restructured out from under it (removing a row, removing a node) so stale indices can't
   * point at the wrong row. */
  private resetEdgeEditState() {
    this.editingEdgeIndex.set(null);
    this.editingEdgeError.set(null);
    this.pendingDiscardEdgeIndex.set(null);
    this.highlightedEdgeIndex.set(null);
  }

  startEditEdge(index: number) {
    if (this.executing()) return;
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
      this.editingEdgeError.set('Select both a from and to node.');
      return;
    }
    if (draft.from === draft.to) {
      this.editingEdgeError.set('A node cannot depend on itself.');
      return;
    }
    const others = this.editDependencies().filter((_, i) => i !== idx);
    if (others.some(d => d.from === draft.from && d.to === draft.to)) {
      this.editingEdgeError.set('This dependency already exists.');
      return;
    }
    const path = this.findPath(others, draft.to, draft.from);
    if (path) {
      const cycleNames = [draft.from, ...path].map(id => this.labelFor(id));
      this.editingEdgeError.set(`This would create a cycle: ${cycleNames.join(' → ')}.`);
      return;
    }

    const updated = [...this.editDependencies()];
    updated[idx] = { ...draft };
    this.editDependencies.set(updated);
    this.editingEdgeIndex.set(null);
    this.pendingDiscardEdgeIndex.set(null);
  }

  /** Clicking an edge in the graph highlights its row in the sidebar (and shows a delete icon at
   * its midpoint on the canvas — see `highlightedEdgeMidpoint`) so the user knows what they'd be
   * acting on — it doesn't enter edit mode itself. */
  highlightEdge(edge: GraphEdge) {
    const idx = this.editDependencies().findIndex(d => d.from === edge.from && d.to === edge.to);
    this.highlightedEdgeIndex.set(idx >= 0 ? idx : null);
  }

  highlightedEdgeMidpoint = computed(() => {
    const idx = this.highlightedEdgeIndex();
    if (idx === null) return null;
    const edge = this.edges()[idx];
    if (!edge) return null;
    return { x: (edge.points[0].x + edge.points[1].x) / 2, y: (edge.points[0].y + edge.points[1].y) / 2 };
  });

  deleteHighlightedEdge() {
    const idx = this.highlightedEdgeIndex();
    if (idx === null) return;
    this.removeEdge(idx);
  }

  /** Deselects everything on the canvas — the node detail panel, an in-progress connect-drag, and
   * a highlighted edge's delete icon — so clicking empty canvas space acts as "cancel/deselect." */
  clearCanvasSelection() {
    this.connectingFromNodeId.set(null);
    this.highlightedEdgeIndex.set(null);
  }

  // --- Mouse-drawn dependencies: click a node to select it (reveals a small connector handle at
  // its bottom edge), click that handle, then click another node to wire a dependency between them. ---
  connectingFromNodeId = signal<string | null>(null);

  startConnecting(nodeId: string) {
    if (this.executing()) return;
    this.connectingFromNodeId.set(this.connectingFromNodeId() === nodeId ? null : nodeId);
  }

  /** Routes a node click to either completing/canceling an in-progress connection, or the normal
   * select-for-detail-panel behavior. Suppressed right after an actual drag (see startNodeDrag)
   * so releasing a dragged node doesn't also toggle its selection. */
  onNodeClick(nodeId: string) {
    if (this.dragMoved) {
      this.dragMoved = false;
      return;
    }
    const from = this.connectingFromNodeId();
    if (from) {
      this.connectingFromNodeId.set(null);
      if (from !== nodeId) this.tryConnect(from, nodeId);
      return;
    }
    this.selectNode(nodeId);
  }

  // --- Drag an existing node to reposition it on the canvas. Once dragged, the node keeps that
  // exact spot (see the `hasManualPosition` check in nodes()) instead of dagre's auto-layout,
  // until "Reset Layout" (resetLayout()) clears every node's position back to null. ---
  private draggingNodeId: string | null = null;
  private dragPointerStartX = 0;
  private dragPointerStartY = 0;
  private dragNodeStartX = 0;
  private dragNodeStartY = 0;
  private dragMoved = false;
  private readonly onNodeDragMove = (e: MouseEvent) => this.handleNodeDragMove(e);
  private readonly onNodeDragUp = () => this.stopNodeDrag();

  startNodeDrag(event: MouseEvent, node: GraphNode) {
    if (this.executing() || this.connectingFromNodeId()) return;
    event.stopPropagation();
    this.draggingNodeId = node.id;
    this.dragPointerStartX = event.clientX;
    this.dragPointerStartY = event.clientY;
    this.dragNodeStartX = node.x;
    this.dragNodeStartY = node.y;
    this.dragMoved = false;
    window.addEventListener('mousemove', this.onNodeDragMove);
    window.addEventListener('mouseup', this.onNodeDragUp);
  }

  private handleNodeDragMove(event: MouseEvent) {
    const id = this.draggingNodeId;
    if (!id) return;
    const dx = event.clientX - this.dragPointerStartX;
    const dy = event.clientY - this.dragPointerStartY;
    // Small threshold so a plain click (mousedown+mouseup with a pixel or two of jitter) doesn't
    // get treated as a drag and start pinning the node in place.
    if (!this.dragMoved && Math.hypot(dx, dy) < 3) return;
    this.dragMoved = true;
    const x = Math.max(0, this.dragNodeStartX + dx);
    const y = Math.max(0, this.dragNodeStartY + dy);
    this.editNodes.set(this.editNodes().map(n => (n.id === id ? { ...n, x, y } : n)));
  }

  private stopNodeDrag() {
    window.removeEventListener('mousemove', this.onNodeDragMove);
    window.removeEventListener('mouseup', this.onNodeDragUp);
    this.draggingNodeId = null;
  }

  private tryConnect(from: string, to: string) {
    if (this.executing()) return;
    this.addEdgeError.set(null);
    if (this.editDependencies().some(d => d.from === from && d.to === to)) return;

    const path = this.findPath(this.editDependencies(), to, from);
    if (path) {
      const cycleNames = [from, ...path].map(id => this.labelFor(id));
      this.addEdgeError.set(`This would create a cycle: ${cycleNames.join(' → ')}.`);
      return;
    }

    this.editDependencies.set([...this.editDependencies(), { from, to }]);
  }

  // --- Inline canvas rename: double-click a node's title to edit its label right there, instead
  // of only via the sidebar's "Nodes in plan" list. ---
  renamingNodeId = signal<string | null>(null);
  renamingDraft = signal('');

  startRenaming(node: GraphNode) {
    if (this.executing()) return;
    this.renamingNodeId.set(node.id);
    this.renamingDraft.set(node.label);
  }

  commitRenaming() {
    const nodeId = this.renamingNodeId();
    if (!nodeId) return;
    this.setNodeLabel(nodeId, this.renamingDraft());
    this.renamingNodeId.set(null);
  }

  cancelRenaming() {
    this.renamingNodeId.set(null);
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
    if (!plan || this.executing()) return;
    const requests = this.editNodes().map(n => ({
      id: n.id,
      aiRequestId: n.aiRequestId,
      label: n.label,
      isFinalOutput: n.isFinalOutput,
      positionX: n.x,
      positionY: n.y,
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
    if (!plan || this.isDirty()) return;
    this.executing.set(true);
    this.nodeStatuses.set(new Map());
    this.nodeStartTimes.set(new Map());
    this.latestRuns.set(new Map());
    this.currentPlanRun.set(null);
    this.nowTick.set(Date.now());
    this.tickHandle = setInterval(() => this.nowTick.set(Date.now()), 100);

    try {
      const planRun = await this.api.executeExecutionPlanStream(plan.id, (evt: PlanExecutionEvent) => {
        if (evt.kind === 0 && evt.planNodeId) {
          const statuses = new Map(this.nodeStatuses());
          statuses.set(evt.planNodeId, 'running');
          this.nodeStatuses.set(statuses);

          const starts = new Map(this.nodeStartTimes());
          starts.set(evt.planNodeId, new Date(evt.timestamp).getTime());
          this.nodeStartTimes.set(starts);
        } else if (evt.kind === 1 && evt.planNodeId && evt.run) {
          const statuses = new Map(this.nodeStatuses());
          statuses.set(evt.planNodeId, evt.run.status === 3 ? 'completed' : evt.run.status === 5 ? 'canceled' : 'failed');
          this.nodeStatuses.set(statuses);

          const runs = new Map(this.latestRuns());
          runs.set(evt.planNodeId, evt.run);
          this.latestRuns.set(runs);
        }
      });
      this.currentPlanRun.set(planRun);
      this.planRuns.set(await this.api.listPlanRuns(plan.id));
    } finally {
      this.executing.set(false);
      if (this.tickHandle) {
        clearInterval(this.tickHandle);
        this.tickHandle = null;
      }
    }
  }

  /** Live elapsed seconds (one decimal) for a still-running node, ticking smoothly; null once it has a result. */
  elapsedLabel(nodeId: string): string | null {
    const start = this.nodeStartTimes().get(nodeId);
    if (start === undefined) return null;
    return Math.max(0, (this.nowTick() - start) / 1000).toFixed(1);
  }

  /** Whole-second duration for a completed/failed run, shown inside the status badge. */
  runSeconds(run: ExecutionRun): number {
    return Math.round((timeSpanToMs(run.totalDuration) ?? 0) / 1000);
  }

  statusLabel(status: GraphNode['status']): string {
    switch (status) {
      case 'running': return 'Running…';
      case 'completed': return 'Success';
      case 'failed': return 'Error';
      case 'canceled': return 'Canceled';
      default: return '';
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
      // the plan-run payload small) — fetch each one and key by planNodeId so nodes/detail panel
      // can look theirs up the same way the live execute() stream does via `latestRuns`. Runs
      // recorded before node identity shipped have a null planNodeId; their (backfilled) node id
      // equals aiRequestId, so falling back to that still attributes them correctly.
      const runs = await Promise.all(run.executionRunIds.map(id => this.api.getRun(id)));
      const runsMap = new Map(runs.map(r => [r.planNodeId ?? r.aiRequestId, r]));
      const statuses = new Map<string, GraphNode['status']>();
      for (const r of runs) {
        statuses.set(r.planNodeId ?? r.aiRequestId, r.status === 3 ? 'completed' : r.status === 5 ? 'canceled' : 'failed');
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
    } else {
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
