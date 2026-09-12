import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/guards/auth.guard';
import { AppLayoutComponent } from './layout/app-layout.component';
import { LoginComponent } from './pages/login/login.component';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { AgentDashboardComponent } from './pages/agent-dashboard/agent-dashboard.component';
import { IncomingCallComponent } from './pages/incoming-call/incoming-call.component';
import { ActiveCallComponent } from './pages/active-call/active-call.component';
import { CustomersComponent } from './pages/customers/customers.component';
import { CallHistoryComponent } from './pages/call-history/call-history.component';
import { CallDetailsComponent } from './pages/call-details/call-details.component';
import { ReportsComponent } from './pages/reports/reports.component';
import { AgentsComponent } from './pages/agents/agents.component';
import { QueueManagementComponent } from './pages/queues/queue-management.component';
import { UnauthorizedComponent } from './pages/unauthorized/unauthorized.component';
import { SettingsComponent } from './pages/settings/settings.component';
import { UsersComponent } from './pages/users/users.component';
import { AuditLogsComponent } from './pages/audit-logs/audit-logs.component';

export const appRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'login', component: LoginComponent },
  { path: 'unauthorized', component: UnauthorizedComponent },
  {
    path: '',
    component: AppLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      {
        path: 'agent-dashboard',
        component: AgentDashboardComponent,
        canActivate: [roleGuard],
        data: { roles: ['Agent', 'Supervisor', 'Admin'] }
      },
      { path: 'incoming-call', component: IncomingCallComponent },
      { path: 'active-call', component: ActiveCallComponent },
      { path: 'customers', component: CustomersComponent },
      {
        path: 'agents',
        component: AgentsComponent,
        canActivate: [roleGuard],
        data: { roles: ['Admin', 'Supervisor'] }
      },
      {
        path: 'queues',
        component: QueueManagementComponent,
        canActivate: [roleGuard],
        data: { roles: ['Admin', 'Supervisor'] }
      },
      { path: 'call-history', component: CallHistoryComponent },
      { path: 'call-history/:id', component: CallDetailsComponent },
      {
        path: 'reports',
        component: ReportsComponent,
        canActivate: [roleGuard],
        data: { roles: ['Admin', 'Supervisor'] }
      },
      {
        path: 'users',
        component: UsersComponent,
        canActivate: [roleGuard],
        data: { roles: ['Admin'] }
      },
      {
        path: 'audit-logs',
        component: AuditLogsComponent,
        canActivate: [roleGuard],
        data: { roles: ['Admin'] }
      },
      {
        path: 'settings',
        component: SettingsComponent,
        canActivate: [roleGuard],
        data: { roles: ['Admin'] }
      }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
