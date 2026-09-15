import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, Subscription } from 'rxjs';
import {
  Workspace, WorkspaceVariable, AiRequestDefinition, ExecutionRun, BenchmarkResponse,
  ComparisonRow, ProviderModel, ModelCatalogProviderStatus, ProviderCredentialStatus,
  ExecutionPlan, PlanDetailResponse, ExecutionPlanRun, AiStreamEvent, PlanExecutionEvent,
  ObservedModelStatistics, Attachment,
} from './models';

export interface CreateRequestBody {
  name: string;
  description?: string | null;
  providerId: string;
  modelId: string;
  systemPrompt?: string | null;
  cachedContextText?: string | null;
  userContextText?: string | null;
  streamingEnabled: boolean;
  maxOutputTokens?: number | null;
  reasoningEffort?: string | null;
  promptCacheKey?: string | null;
  structuredOutputSchema?: string | null;
  tags?: string[] | null;
  cachedContextAttachmentIds?: string[] | null;
  userContextAttachmentIds?: string[] | null;
}

/**
 * A promise that resolves to the completed result, plus a `cancel()` that aborts the in-flight
 * request/stream. Cancelling resolves the promise with `null` rather than rejecting, since the
 * server still persists a Canceled ExecutionRun — callers re-fetch run history to see it.
 */
export interface CancelableExecution<T> {
  promise: Promise<T | null>;
  cancel: () => void;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  health() {
    return firstValueFrom(this.http.get<{ status: string }>(`${this.base}/health`));
  }

  // Providers
  providerStatus() {
    return firstValueFrom(this.http.get<Record<string, ProviderCredentialStatus>>(`${this.base}/providers/status`));
  }

  // Workspaces
  listWorkspaces() {
    return firstValueFrom(this.http.get<Workspace[]>(`${this.base}/workspaces`));
  }

  getWorkspace(id: string) {
    return firstValueFrom(this.http.get<Workspace>(`${this.base}/workspaces/${id}`));
  }

  createWorkspace(name: string, description?: string, tags?: string[]) {
    return firstValueFrom(this.http.post<Workspace>(`${this.base}/workspaces`, { name, description, tags }));
  }

  updateWorkspace(id: string, name: string, description?: string, tags?: string[]) {
    return firstValueFrom(this.http.put<Workspace>(`${this.base}/workspaces/${id}`, { name, description, tags }));
  }

  deleteWorkspace(id: string) {
    return firstValueFrom(this.http.delete<void>(`${this.base}/workspaces/${id}`));
  }

  listVariables(workspaceId: string) {
    return firstValueFrom(this.http.get<WorkspaceVariable[]>(`${this.base}/workspaces/${workspaceId}/variables`));
  }

  setVariable(workspaceId: string, name: string, value: string) {
    return firstValueFrom(this.http.put<void>(`${this.base}/workspaces/${workspaceId}/variables/${encodeURIComponent(name)}`, { value }));
  }

  exportWorkspace(workspaceId: string, format: 'csv' | 'json') {
    return firstValueFrom(this.http.get(`${this.base}/workspaces/${workspaceId}/export?format=${format}`, { responseType: 'text' }));
  }

  // Attachments
  listAttachments(workspaceId: string) {
    return firstValueFrom(this.http.get<Attachment[]>(`${this.base}/workspaces/${workspaceId}/attachments`));
  }

  uploadAttachment(workspaceId: string, file: File) {
    const form = new FormData();
    form.append('file', file, file.name);
    return firstValueFrom(this.http.post<Attachment>(`${this.base}/workspaces/${workspaceId}/attachments`, form));
  }

  deleteAttachment(id: string) {
    return firstValueFrom(this.http.delete<void>(`${this.base}/attachments/${id}`));
  }

  // Requests
  listRequests(workspaceId: string) {
    return firstValueFrom(this.http.get<AiRequestDefinition[]>(`${this.base}/workspaces/${workspaceId}/requests`));
  }

  getRequest(id: string) {
    return firstValueFrom(this.http.get<AiRequestDefinition>(`${this.base}/requests/${id}`));
  }

  createRequest(workspaceId: string, body: CreateRequestBody) {
    return firstValueFrom(this.http.post<AiRequestDefinition>(`${this.base}/workspaces/${workspaceId}/requests`, body));
  }

  updateRequest(id: string, body: CreateRequestBody) {
    return firstValueFrom(this.http.put<AiRequestDefinition>(`${this.base}/requests/${id}`, body));
  }

  deleteRequest(id: string) {
    return firstValueFrom(this.http.delete<void>(`${this.base}/requests/${id}`));
  }

  cloneRequest(id: string, newName?: string) {
    const q = newName ? `?newName=${encodeURIComponent(newName)}` : '';
    return firstValueFrom(this.http.post<AiRequestDefinition>(`${this.base}/requests/${id}/clone${q}`, {}));
  }

  listRuns(requestId: string) {
    return firstValueFrom(this.http.get<ExecutionRun[]>(`${this.base}/requests/${requestId}/runs`));
  }

  getRun(id: string) {
    return firstValueFrom(this.http.get<ExecutionRun>(`${this.base}/runs/${id}`));
  }

  archiveRun(id: string, isArchived: boolean) {
    return firstValueFrom(this.http.post<ExecutionRun>(`${this.base}/runs/${id}/archive`, { isArchived }));
  }

  /**
   * Cancel aborts the underlying HTTP request; the server still persists a Canceled run.
   * `override` runs the SAME saved request against a different provider/model for that call only
   * (never persisted onto the saved request) — used for multi-model comparison runs.
   */
  executeRequest(id: string, override?: { providerId: string; modelId: string }): CancelableExecution<ExecutionRun> {
    let settled = false;
    let resolveFn!: (v: ExecutionRun | null) => void;
    let subscription: Subscription;
    const promise = new Promise<ExecutionRun | null>((resolve, reject) => {
      resolveFn = resolve;
      const body = override ? { providerId: override.providerId, modelId: override.modelId } : {};
      subscription = this.http.post<ExecutionRun>(`${this.base}/requests/${id}/execute`, body).subscribe({
        next: (run) => { settled = true; resolve(run); },
        error: (err) => { if (!settled) { settled = true; reject(err); } },
      });
    });
    const cancel = () => {
      if (settled) return;
      settled = true;
      subscription?.unsubscribe();
      resolveFn(null);
    };
    return { promise, cancel };
  }

  benchmark(id: string, count: number) {
    return firstValueFrom(this.http.post<BenchmarkResponse>(`${this.base}/requests/${id}/benchmark`, { count }));
  }

  /**
   * Streams live AiStreamEvents via SSE, calling onEvent for each, and resolves with the final
   * persisted ExecutionRun. Cancel closes the EventSource, which drops the server connection —
   * the server still persists the resulting Canceled run.
   */
  executeRequestStream(id: string, onEvent: (evt: AiStreamEvent) => void, override?: { providerId: string; modelId: string }): CancelableExecution<ExecutionRun> {
    let settled = false;
    let resolveFn!: (v: ExecutionRun | null) => void;
    let source: EventSource;
    const promise = new Promise<ExecutionRun | null>((resolve, reject) => {
      resolveFn = resolve;
      const q = override ? `?providerId=${encodeURIComponent(override.providerId)}&modelId=${encodeURIComponent(override.modelId)}` : '';
      source = new EventSource(`${this.base}/requests/${id}/execute-stream${q}`);
      source.addEventListener('stream-event', (e: MessageEvent) => onEvent(JSON.parse(e.data)));
      source.addEventListener('run-completed', (e: MessageEvent) => {
        settled = true;
        source.close();
        resolve(JSON.parse(e.data));
      });
      source.onerror = () => {
        source.close();
        if (!settled) { settled = true; reject(new Error('Stream connection error')); }
      };
    });
    const cancel = () => {
      if (settled) return;
      settled = true;
      source?.close();
      resolveFn(null);
    };
    return { promise, cancel };
  }

  // Comparison / Diff
  comparison(requestIds: string[]) {
    return firstValueFrom(this.http.get<ComparisonRow[]>(`${this.base}/comparison?requestIds=${requestIds.join(',')}`));
  }

  diff(left: string, right: string) {
    return firstValueFrom(this.http.post<{ kind: 'json' | 'text'; json?: unknown; text?: unknown }>(`${this.base}/diff`, { left, right }));
  }

  // Model catalog
  listModels() {
    return firstValueFrom(this.http.get<ProviderModel[]>(`${this.base}/models`));
  }

  modelCatalogStatus() {
    return firstValueFrom(this.http.get<Record<string, ModelCatalogProviderStatus>>(`${this.base}/models/status`));
  }

  refreshModels(providerId?: string) {
    const q = providerId ? `?providerId=${providerId}` : '';
    return firstValueFrom(this.http.post(`${this.base}/models/refresh${q}`, {}));
  }

  observedModelStats() {
    return firstValueFrom(this.http.get<ObservedModelStatistics[]>(`${this.base}/models/observed-stats`));
  }

  // Execution plans
  listExecutionPlans(workspaceId: string) {
    return firstValueFrom(this.http.get<ExecutionPlan[]>(`${this.base}/workspaces/${workspaceId}/execution-plans`));
  }

  createExecutionPlan(workspaceId: string, name: string, requests: { aiRequestId: string; isFinalOutput: boolean }[], dependencies: { from: string; to: string }[]) {
    return firstValueFrom(this.http.post<ExecutionPlan>(`${this.base}/workspaces/${workspaceId}/execution-plans`, { name, requests, dependencies }));
  }

  getExecutionPlan(id: string) {
    return firstValueFrom(this.http.get<PlanDetailResponse>(`${this.base}/execution-plans/${id}`));
  }

  updateExecutionPlan(id: string, name: string, requests: { aiRequestId: string; isFinalOutput: boolean }[], dependencies: { from: string; to: string }[]) {
    return firstValueFrom(this.http.put<ExecutionPlan>(`${this.base}/execution-plans/${id}`, { name, requests, dependencies }));
  }

  deleteExecutionPlan(id: string) {
    return firstValueFrom(this.http.delete<void>(`${this.base}/execution-plans/${id}`));
  }

  listPlanRuns(id: string) {
    return firstValueFrom(this.http.get<ExecutionPlanRun[]>(`${this.base}/execution-plans/${id}/runs`));
  }

  executeExecutionPlan(id: string) {
    return firstValueFrom(this.http.post<ExecutionPlanRun>(`${this.base}/execution-plans/${id}/execute`, {}));
  }

  executeExecutionPlanStream(id: string, onEvent: (evt: PlanExecutionEvent) => void): Promise<ExecutionPlanRun> {
    return new Promise((resolve, reject) => {
      const source = new EventSource(`${this.base}/execution-plans/${id}/execute-stream`);
      source.addEventListener('plan-event', (e: MessageEvent) => onEvent(JSON.parse(e.data)));
      source.addEventListener('plan-completed', (e: MessageEvent) => {
        source.close();
        resolve(JSON.parse(e.data));
      });
      source.onerror = () => {
        source.close();
        reject(new Error('Stream connection error'));
      };
    });
  }
}
