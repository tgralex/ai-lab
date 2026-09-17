import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, inject, signal, SecurityContext } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer } from '@angular/platform-browser';
import { marked } from 'marked';
import { ExecutionRun, ExecutionStatus, ExecutionStatusLabel } from '../core/models';
import { Icon } from './icon';
import { formatTimeSpan, formatCost, formatTokensPerSecond } from './format';
import { PersistSizeDirective } from './persist-size.directive';

type ResponseViewMode = 'rendered' | 'plain' | 'raw';

// Gemini reports finish reasons in uppercase ('MAX_TOKENS'), unlike the other three providers.
const TRUNCATED_FINISH_REASONS = new Set(['max_tokens', 'incomplete', 'length', 'MAX_TOKENS']);

@Component({
  selector: 'app-run-detail',
  standalone: true,
  imports: [CommonModule, Icon, PersistSizeDirective],
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

  // JSON.stringify with a properly-escaped (still-valid-JSON) string — computed once and reused
  // by both the plain-text and highlighted-HTML views below, since key-detection only stays
  // unambiguous on the escaped form (a real newline inside an unescaped value could otherwise be
  // mistaken for a new key line).
  private prettyJsonRaw = computed<string | null>(() => {
    try {
      return JSON.stringify(JSON.parse(this.displayOutput()), null, 2);
    } catch {
      return null;
    }
  });

  prettyJsonOutput = computed(() => {
    const raw = this.prettyJsonRaw();
    return raw === null ? this.displayOutput() : unescapeForDisplay(raw);
  });

  // Same content as prettyJsonOutput, but with property names bolded/colored so they read apart
  // from their values — built from the escaped form so key lines ("key": ) are unambiguous, then
  // HTML-escaped and newline/tab-unescaped for display after the highlighting spans are inserted.
  prettyJsonHtml = computed(() => {
    const raw = this.prettyJsonRaw();
    if (raw === null) return '';
    const escaped = escapeHtml(raw);
    const highlighted = escaped.replace(
      /^(\s*)"((?:[^"\\]|\\.)*)":/gm,
      '$1<span class="font-semibold text-sky-700 dark:text-sky-400">"$2"</span>:',
    );
    return this.sanitizer.sanitize(SecurityContext.HTML, unescapeForDisplay(highlighted)) ?? '';
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

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// JSON.stringify escapes newlines/tabs *inside* string values as literal "\n"/"\t" (valid JSON,
// but unreadable — a multi-paragraph field renders as one run-on line). Unescape them for display;
// the Raw JSON tab still shows the untouched wire format.
function unescapeForDisplay(text: string): string {
  return text.replace(/\\n/g, '\n').replace(/\\t/g, '\t');
}
