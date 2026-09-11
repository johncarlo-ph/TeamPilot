import { Routes } from '@angular/router';
import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'projects' },
      {
        path: 'projects',
        loadComponent: () =>
          import('./features/projects/project-list/project-list').then((m) => m.ProjectList),
      },
      {
        path: 'projects/:projectId/board',
        loadComponent: () => import('./features/board/board').then((m) => m.Board),
      },
      {
        path: 'projects/:projectId/agents',
        loadComponent: () => import('./features/agents/agents').then((m) => m.Agents),
      },
      {
        path: 'projects/:projectId/pipeline-runs',
        loadComponent: () =>
          import('./features/pipeline-runs/pipeline-runs').then((m) => m.PipelineRuns),
      },
      {
        path: 'tickets/:id',
        loadComponent: () =>
          import('./features/ticket-detail/ticket-detail').then((m) => m.TicketDetail),
      },
      {
        path: 'admin/users',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/users/users').then((m) => m.Users),
      },
      {
        path: 'admin/audit-log',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/audit-log/audit-log').then((m) => m.AuditLog),
      },
    ],
  },
  { path: '**', redirectTo: 'projects' },
];
