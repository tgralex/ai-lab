import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Workspace, ProviderCredentialStatus } from '../../core/models';
import { Icon } from '../../shared/icon';

@Component({
  selector: 'app-workspace-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, Icon],
  templateUrl: './workspace-picker.html',
})
export class WorkspacePicker {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  workspaces = signal<Workspace[]>([]);
  providerStatus = signal<Record<string, ProviderCredentialStatus>>({});
  newWorkspaceName = signal('');
  loading = signal(true);

  constructor() {
    void this.load();
  }

  async load() {
    this.loading.set(true);
    const [workspaces, status] = await Promise.all([this.api.listWorkspaces(), this.api.providerStatus()]);
    this.workspaces.set(workspaces);
    this.providerStatus.set(status);
    this.loading.set(false);
  }

  async createWorkspace() {
    const name = this.newWorkspaceName().trim();
    if (!name) return;
    const workspace = await this.api.createWorkspace(name);
    this.newWorkspaceName.set('');
    void this.router.navigate(['/w', workspace.id]);
  }

  open(workspace: Workspace) {
    void this.router.navigate(['/w', workspace.id]);
  }
}
