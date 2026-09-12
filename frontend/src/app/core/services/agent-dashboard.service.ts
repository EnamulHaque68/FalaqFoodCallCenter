import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AgentStatus, AgentDashboard } from '../models/agent-dashboard.models';
import { AgentResponse } from '../models/agent.models';

@Injectable({ providedIn: 'root' })
export class AgentDashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/agents`;

  getAll(): Observable<AgentResponse[]> {
    return this.http.get<AgentResponse[]>(this.baseUrl);
  }

  getMyDashboard(): Observable<AgentDashboard> {
    return this.http.get<AgentDashboard>(`${this.baseUrl}/me/dashboard`);
  }

  updateMyStatus(agentId: string, status: AgentStatus): Observable<AgentDashboardAgent> {
    return this.http.put<AgentDashboardAgent>(`${this.baseUrl}/${agentId}/status`, { status });
  }
}

export interface AgentDashboardAgent {
  id: string;
  userId: string;
  employeeCode: string;
  displayName: string;
  status: AgentStatus;
  createdAt: string;
  updatedAt?: string;
}
