import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, inject, signal, SecurityContext } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer } from '@angular/platform-browser';
import { marked } from 'marked';
import { ExecutionRun, ExecutionStatus, ExecutionStatusLabel } from '../core/models';
import { Icon } from './icon';
import { formatTimeSpan, formatCost, formatTokensPerSecond } from './format';

type ResponseViewMode = 'rendered' | 'plain' | 'raw';

// Gemini reports finish reasons in uppercase ('MAX_TOKENS'), unlike the other three providers.
const TRUNCATED_FINISH_REASONS = new Set(['max_tokens', 'incomplete', 'length', 'MAX_TOKENS']);

@Component({
  selector: 'app-run-detail',
  standalone: true,
  imports: [CommonModule, Icon],
  templateUrl: './run-detail.html',
})
export class RunDetail implements OnChanges {
  @Input() run: ExecutionRun | null = null;
  /** Live text while `executing` and `run` is still null (streaming, not yet persisted). */
  @Input() streamingOutput = '';
  @Input() executing = false;
  @Input() elapsedMs = 0;
  /** False for rows without a per-row timer (e.g. multi-model comparison rows). */
  @Input() showElapsed = true;
  @Input() showStop = false;
  @Output() stop = new EventEmitter<void>();

  private readonly sanitizer = inject(DomSanitizer);

  readonly ExecutionStatusLabel = ExecutionStatusLabel;
  readonly formatTimeSpan = formatTimeSpan;
  readonly formatCost = formatCost;

  // computed() only reacts to signal reads, not plain @Input() fields — so every @Input this
  // component derives a computed() from is mirrored into its own signal here (same pattern as
  // model-dropdown.ts / provider-icon.ts), otherwise the computeds below cache their first value
  // forever and keep showing a stale run's content after the parent rebinds a different one.
  private runSig = signal<ExecutionRun | null>(null);
  private streamingOutputSig = signal('');
  private executingSig = signal(false);
  private elapsedMsSig = signal(0);
  private showElapsedSig = signal(true);
  private showStopSig = signal(false);

  ngOnChanges(changes: SimpleChanges) {
    if (changes['run']) this.runSig.set(this.run);
    if (changes['streamingOutput']) this.streamingOutputSig.set(this.streamingOutput);
    if (changes['executing']) this.executingSig.set(this.executing);
    if (changes['elapsedMs']) this.elapsedMsSig.set(this.elapsedMs);
    if (changes['showElapsed']) this.showElapsedSig.set(this.showElapsed);
    if (changes['showStop']) this.showStopSig.set(this.showStop);
  }

  runValue = computed(() => this.runSig());
  executingValue = computed(() => this.executingSig());
  elapsedMsValue = computed(() => this.elapsedMsSig());
  showElapsedValue = computed(() => this.showElapsedSig());
  showStopValue = computed(() => this.showStopSig());

  responseViewMode = signal<ResponseViewMode>('rendered');
  copied = signal(false);
  private copiedTimeout: ReturnType<typeof setTimeout> | null = null;

  displayOutput = computed(() => this.streamingOutputSig() || this.runSig()?.output || '');

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

  rawResponseJson = computed(() => {
    const run = this.runSig();
    if (!run?.rawProviderResponseJson) return '';
    try {
      return JSON.stringify(JSON.parse(run.rawProviderResponseJson), null, 2);
    } catch {
      return run.rawProviderResponseJson;
    }
  });

  isTruncated = computed(() => {
    const reason = this.runSig()?.finishReason;
    return !!reason && TRUNCATED_FINISH_REASONS.has(reason);
  });

  statusLabel(status: ExecutionStatus): string {
    return ExecutionStatusLabel[status];
  }

  outputTokensPerSecondDisplay(): string {
    const run = this.runSig();
    if (!run?.outputTokensPerSecond) return '—';
    return formatTokensPerSecond(run.outputTokensPerSecond);
  }

  async copyResponse() {
    const text = this.responseViewMode() === 'raw' ? this.rawResponseJson()
      : this.responseViewMode() === 'plain' ? this.displayOutput()
      : this.responseViewMode() === 'rendered' && this.isJsonOutput() ? this.prettyJsonOutput()
      : this.displayOutput();
    if (!text) return;
    await navigator.clipboard.writeText(text);
    this.copied.set(true);
    if (this.copiedTimeout) clearTimeout(this.copiedTimeout);
    this.copiedTimeout = setTimeout(() => this.copied.set(false), 1500);
  }
}
