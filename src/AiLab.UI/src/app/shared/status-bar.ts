import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../core/api.service';
import { BUILD_INFO } from '../core/build-info';

const POLL_INTERVAL_MS = 20_000;

@Component({
  selector: 'app-status-bar',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './status-bar.html',
})
export class StatusBar implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);

  readonly version = BUILD_INFO.version;
  readonly buildTime = new Date(BUILD_INFO.buildTime);

  // null = still checking (first load); true/false once a check has resolved.
  backendAlive = signal<boolean | null>(null);
  private pollHandle: ReturnType<typeof setInterval> | null = null;

  async ngOnInit() {
    await this.checkHealth();
    this.pollHandle = setInterval(() => this.checkHealth(), POLL_INTERVAL_MS);
  }

  ngOnDestroy() {
    if (this.pollHandle) clearInterval(this.pollHandle);
  }

  private async checkHealth() {
    try {
      const result = await this.api.health();
      this.backendAlive.set(result?.status === 'ok');
    } catch {
      this.backendAlive.set(false);
    }
  }
}
