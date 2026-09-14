import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import * as dagre from 'dagre';
import { ApiService } from '../../core/api.service';
import {
  ExecutionPlan, AiRequestDefinition, ExecutionPlanRun, PlanExecutionEvent, ExecutionRun,
  ExecutionLevel, ExecutionPlanValidationResult,
} from '../../core/models';
import { formatTimeSpan, formatCost } from '../../shared/format';

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
  points: { x: number; y: number }[];
}

const NODE_WIDTH = 200;
const NODE_HEIGHT = 76;

@Component({
  selector: 'app-plan-view',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './plan-view.html',
})
export class PlanView implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly formatTimeSpan = formatTimeSpan;
  readonly formatCost = formatCost;

  workspaceId = signal('');
  planId = signal('');
  plan = signal<ExecutionPlan | null>(null);
  levels = signal<ExecutionLevel[]>([]);
  validation = signal<ExecutionPlanValidationResult | null>(null);
  allRequests = signal<AiRequestDefinition[]>([]);
  requestsById = computed(() => new Map(this.allRequests().map(r => [r.id, r])));

  editing = signal(false);
  editName = signal('');
  editRequestIds = signal<Set<string>>(new Set());
  editFinalOutputId = signal<string | null>(null);
  editDependencies = signal<{ from: string; to: string }[]>([]);
  addEdgeFrom = signal<string>('');
  addEdgeTo = signal<string>('');

  executing = signal(false);
  nodeStatuses = signal<Map<string, GraphNode['status']>>(new Map());
  latestRuns = signal<Map<string, ExecutionRun>>(new Map());
  planRuns = signal<ExecutionPlanRun[]>([]);
  currentPlanRun = signal<ExecutionPlanRun | null>(null);

  nodes = computed<GraphNode[]>(() => {
    const plan = this.plan();
    if (!plan) return [];

    const g = new dagre.graphlib.Graph();
    g.setGraph({ rankdir: 'TB', nodesep: 30, ranksep: 60 });
    g.setDefaultEdgeLabel(() => ({}));

    for (const r of plan.requests) {
      g.setNode(r.aiRequestId, { width: NODE_WIDTH, height: NODE_HEIGHT });
    }
    for (const d of plan.dependencies) {
      g.setEdge(d.fromRequestId, d.toRequestId);
    }

    dagre.layout(g);

    const statuses = this.nodeStatuses();
    const runs = this.latestRuns();
    const requestsById = this.requestsById();

    const result: GraphNode[] = [];
    for (const r of plan.requests) {
      const n = g.node(r.aiRequestId);
      const request = requestsById.get(r.aiRequestId);
      if (!request || !n) continue;
      result.push({
        id: r.aiRequestId,
        request,
        x: n.x - NODE_WIDTH / 2,
        y: n.y - NODE_HEIGHT / 2,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        isFinalOutput: r.isFinalOutput,
        latestRun: runs.get(r.aiRequestId) ?? null,
        status: statuses.get(r.aiRequestId) ?? 'idle',
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
    const plan = this.plan();
    const nodesById = new Map(this.nodes().map(n => [n.id, n]));
    if (!plan) return [];

    return plan.dependencies
      .map(d => {
        const from = nodesById.get(d.fromRequestId);
        const to = nodesById.get(d.toRequestId);
        if (!from || !to) return null;
        return {
          points: [
            { x: from.x + from.width / 2, y: from.y + from.height },
            { x: to.x + to.width / 2, y: to.y },
          ],
        };
      })
      .filter((e): e is GraphEdge => e !== null);
  });

  finalOutputRun = computed(() => {
    const plan = this.plan();
    const finalNode = plan?.requests.find(r => r.isFinalOutput);
    if (!finalNode) return null;
    return this.latestRuns().get(finalNode.aiRequestId) ?? null;
  });

  async ngOnInit() {
    this.workspaceId.set(this.route.snapshot.paramMap.get('workspaceId')!);
    this.planId.set(this.route.snapshot.paramMap.get('planId')!);
    await this.loadAll();
  }

  async loadAll() {
    const [detail, requests, runs] = await Promise.all([
      this.api.getExecutionPlan(this.planId()),
      this.api.listRequests(this.workspaceId()),
      this.api.listPlanRuns(this.planId()),
    ]);
    this.plan.set(detail.plan);
    this.levels.set(detail.levels);
    this.validation.set(detail.validation);
    this.allRequests.set(requests);
    this.planRuns.set(runs);
    this.resetEditState();

    if (runs.length > 0) {
      this.applyPlanRunResult(runs[0]);
    }
  }

  resetEditState() {
    const plan = this.plan();
    if (!plan) return;
    this.editName.set(plan.name);
    this.editRequestIds.set(new Set(plan.requests.map(r => r.aiRequestId)));
    this.editFinalOutputId.set(plan.requests.find(r => r.isFinalOutput)?.aiRequestId ?? null);
    this.editDependencies.set(plan.dependencies.map(d => ({ from: d.fromRequestId, to: d.toRequestId })));
  }

  startEditing() {
    this.resetEditState();
    this.editing.set(true);
  }

  toggleEditRequest(id: string) {
    const set = new Set(this.editRequestIds());
    if (set.has(id)) set.delete(id);
    else set.add(id);
    this.editRequestIds.set(set);
  }

  addEdge() {
    const from = this.addEdgeFrom();
    const to = this.addEdgeTo();
    if (!from || !to || from === to) return;
    if (this.editDependencies().some(d => d.from === from && d.to === to)) return;
    this.editDependencies.set([...this.editDependencies(), { from, to }]);
  }

  removeEdge(index: number) {
    this.editDependencies.set(this.editDependencies().filter((_, i) => i !== index));
  }

  async saveEdits() {
    const plan = this.plan();
    if (!plan) return;
    const requests = Array.from(this.editRequestIds()).map(id => ({
      aiRequestId: id,
      isFinalOutput: id === this.editFinalOutputId(),
    }));
    await this.api.updateExecutionPlan(plan.id, this.editName().trim() || plan.name, requests, this.editDependencies());
    this.editing.set(false);
    await this.loadAll();
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

  viewPlanRun(run: ExecutionPlanRun) {
    this.applyPlanRunResult(run);
  }

  private applyPlanRunResult(run: ExecutionPlanRun) {
    this.currentPlanRun.set(run);
    // Historical runs don't carry back individual ExecutionRun objects inline (only ids) —
    // group-level stats still render from `run.groups`; per-node output requires opening that
    // request's own History tab. This keeps the plan-run payload small.
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
