export interface Workspace {
  id: string;
  name: string;
  description: string | null;
  tags: string[];
  createdAt: string;
  updatedAt: string;
}

export interface WorkspaceVariable {
  id: string;
  workspaceId: string;
  name: string;
  value: string;
}

export interface ContentBlock {
  text: string;
  attachmentIds: string[];
}

export interface ReasoningConfig {
  effort: string;
}

export interface RetryPolicy {
  maxRetries: number;
  baseBackoffMs: number;
}

export type FailurePolicy = 0 | 1 | 2; // FailPlan, Retry, ContinueWithError

export interface AiRequestDefinition {
  id: string;
  workspaceId: string;
  name: string;
  description: string | null;
  providerId: string;
  modelId: string;
  systemPrompt: string | null;
  cachedContext: ContentBlock;
  userContext: ContentBlock;
  inputBindings: unknown[];
  structuredOutputSchema: string | null;
  streamingEnabled: boolean;
  maxOutputTokens: number | null;
  temperature: number | null;
  reasoning: ReasoningConfig | null;
  providerSettings: Record<string, string>;
  promptCacheKey: string | null;
  failurePolicy: FailurePolicy;
  retryPolicy: RetryPolicy;
  tags: string[];
  notes: string | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

export type ExecutionStatus = 0 | 1 | 2 | 3 | 4 | 5; // Idle Running Streaming Completed Failed Canceled

export const ExecutionStatusLabel: Record<ExecutionStatus, string> = {
  0: 'Idle',
  1: 'Running',
  2: 'Streaming',
  3: 'Completed',
  4: 'Failed',
  5: 'Canceled',
};

export interface TokenUsage {
  inputTokens: number;
  cachedInputTokens: number;
  outputTokens: number;
  reasoningTokens: number;
  totalTokens: number;
  uncachedInputTokens: number;
  cacheHitPercentage: number;
}

export interface FailureInfo {
  exceptionType: string | null;
  message: string | null;
  httpStatus: number | null;
  providerError: string | null;
  streamingStarted: boolean;
  retryCount: number;
}

export interface RequestSnapshot {
  providerId: string;
  modelId: string;
  systemPrompt: string | null;
  resolvedCachedContext: string | null;
  resolvedUserContext: string | null;
  resolvedBindings: Record<string, string>;
  attachmentHashes: string[];
  reasoningEffort: string | null;
  streaming: boolean;
  maxOutputTokens: number | null;
  temperature: number | null;
  structuredOutputSchema: string | null;
  promptCacheKey: string | null;
  providerSettings: Record<string, string>;
  capturedAt: string;
}

export interface ExecutionRun {
  id: string;
  aiRequestId: string;
  planNodeId: string | null;
  providerId: string;
  requestedModel: string | null;
  actualModel: string | null;
  status: ExecutionStatus;
  startedAt: string | null;
  firstResponseEventAt: string | null;
  firstOutputTokenAt: string | null;
  finishedAt: string | null;
  totalDuration: string | null;
  timeToFirstResponseEvent: string | null;
  timeToFirstOutputToken: string | null;
  generationDuration: string | null;
  usage: TokenUsage;
  estimatedInputCost: number | null;
  estimatedCachedInputCost: number | null;
  estimatedOutputCost: number | null;
  estimatedTotalCost: number | null;
  output: string | null;
  rawProviderResponseJson: string | null;
  normalizedProviderRequestJson: string | null;
  responseId: string | null;
  finishReason: string | null;
  requestSizeBytes: number | null;
  responseSizeBytes: number | null;
  failure: FailureInfo | null;
  retryCount: number;
  snapshot: RequestSnapshot;
  outputTokensPerSecond: number | null;
  isArchived: boolean;
}

export type AiStreamEventKind = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7; // Started FirstProtocolEvent OutputTextDelta ReasoningDelta ToolCallDelta UsageUpdate Completed Error

export interface AiStreamEvent {
  kind: AiStreamEventKind;
  timestamp: string;
  textDelta: string | null;
  errorMessage: string | null;
}

export interface DescriptiveStatsSummary {
  count: number;
  min: number;
  max: number;
  mean: number;
  median: number;
  p50: number;
  p90: number;
  p95: number;
  stdDev: number;
}

export interface BenchmarkResult {
  runCount: number;
  successCount: number;
  failureCount: number;
  canceledCount: number;
  duration: DescriptiveStatsSummary;
  ttft: DescriptiveStatsSummary;
  generationDuration: DescriptiveStatsSummary;
  inputTokens: DescriptiveStatsSummary;
  outputTokens: DescriptiveStatsSummary;
  totalTokens: DescriptiveStatsSummary;
  cost: DescriptiveStatsSummary;
  outputTokensPerSecond: DescriptiveStatsSummary;
}

export interface BenchmarkResponse {
  runs: ExecutionRun[];
  stats: BenchmarkResult;
}

export interface ComparisonRow {
  requestId: string;
  requestName: string;
  provider: string;
  model: string;
  reasoning: string | null;
  temperature: number | null;
  runId: string | null;
  totalMs: number | null;
  ttftMs: number | null;
  generationMs: number | null;
  inputTokens: number;
  cachedTokens: number;
  cachePercent: number;
  outputTokens: number;
  reasoningTokens: number;
  tokensPerSecond: number | null;
  cost: number | null;
  status: string;
}

export type ModelRefreshSource = 0 | 1 | 2; // Live SeedOnly Mixed

export interface ProviderModel {
  providerId: string;
  modelId: string;
  displayName: string;
  isActive: boolean;
  isDeprecated: boolean;
  recommended: boolean;
  contextWindowTokens: number | null;
  maxOutputTokens: number | null;
  supportsReasoning: boolean;
  supportedReasoningLevels: string[];
  supportsStreaming: boolean;
  supportsStructuredOutput: boolean;
  supportsToolCalling: boolean;
  supportsPromptCacheKey: boolean;
  inputModalities: string[];
  outputModalities: string[];
  inputPricePerMillion: number | null;
  cachedInputPricePerMillion: number | null;
  outputPricePerMillion: number | null;
  source: ModelRefreshSource;
}

export interface Attachment {
  id: string;
  workspaceId: string;
  filename: string;
  mimeType: string;
  sizeBytes: number;
  sha256: string;
  createdAt: string;
}

export interface ObservedModelStatistics {
  providerId: string;
  modelId: string;
  sampleCount: number;
  ttft: DescriptiveStatsSummary;
  totalLatency: DescriptiveStatsSummary;
  outputTokensPerSecond: DescriptiveStatsSummary;
  averageInputTokens: number;
  averageOutputTokens: number;
  averageReasoningTokens: number;
  averageCost: number;
  medianCost: number;
  cacheHitRate: number;
}

export interface ModelCatalogProviderStatus {
  providerId: string;
  lastRefreshAt: string | null;
  refreshSource: ModelRefreshSource;
  refreshStatus: string;
}

export interface ProviderCredentialStatus {
  configured: boolean;
}

export interface ExecutionPlanRequest {
  id: string;
  aiRequestId: string;
  label: string | null;
  isFinalOutput: boolean;
  positionX: number | null;
  positionY: number | null;
}

export interface ExecutionPlanDependency {
  fromNodeId: string;
  toNodeId: string;
}

export interface ExecutionPlan {
  id: string;
  workspaceId: string;
  name: string;
  requests: ExecutionPlanRequest[];
  dependencies: ExecutionPlanDependency[];
  createdAt: string;
  updatedAt: string;
}

export interface ExecutionLevel {
  index: number;
  nodeIds: string[];
}

export interface ExecutionPlanValidationResult {
  isValid: boolean;
  errors: string[];
}

export interface PlanDetailResponse {
  plan: ExecutionPlan;
  levels: ExecutionLevel[];
  validation: ExecutionPlanValidationResult;
}

export interface ExecutionGroupRun {
  levelIndex: number;
  nodeIds: string[];
  executionRunIds: string[];
  wallClockDuration: string;
  cumulativeRequestDuration: string;
}

export interface ExecutionPlanRun {
  id: string;
  executionPlanId: string;
  status: ExecutionStatus;
  startedAt: string | null;
  finishedAt: string | null;
  wallClockDuration: string | null;
  cumulativeRequestDuration: string;
  totalInputTokens: number;
  totalCachedInputTokens: number;
  totalOutputTokens: number;
  totalReasoningTokens: number;
  totalEstimatedCost: number;
  groups: ExecutionGroupRun[];
  executionRunIds: string[];
}

export type PlanExecutionEventKind = 0 | 1 | 2; // NodeStarted NodeCompleted PlanCompleted

export interface PlanExecutionEvent {
  kind: PlanExecutionEventKind;
  timestamp: string;
  planNodeId: string | null;
  run: ExecutionRun | null;
}
