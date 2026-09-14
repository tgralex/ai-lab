import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/workspace-picker/workspace-picker').then(m => m.WorkspacePicker) },
  { path: 'w/:workspaceId', loadComponent: () => import('./features/workspace-shell/workspace-shell').then(m => m.WorkspaceShell) },
  { path: 'w/:workspaceId/plans/:planId', loadComponent: () => import('./features/plan-view/plan-view').then(m => m.PlanView) },
];
