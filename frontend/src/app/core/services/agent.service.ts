import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Agent,
  AgentDetails,
  AgentCallItem,
  CreateAgentRequest,
  UpdateAgentRequest,
  AgentFilter
} from '../models/agent.models';
import { AgentStatus } from '../models/agent-dashboard.models';

@Injectable({ providedIn: 'root' })
export class AgentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/agents`;

  getAll(filter?: AgentFilter): Observable<Agent[]> {
    let params = new HttpParams();
    if (filter) {
      if (filter.search?.trim()) {
        params = params.set('search', filter.search.trim());
      }
      if (filter.status !== undefined && filter.status !== '') {
        params = params.set('status', filter.status);
      }
      if (filter.team?.trim()) {
        params = params.set('team', filter.team.trim());
      }
      if (filter.isActive !== undefined && filter.isActive !== '') {
        params = params.set('isActive', String(filter.isActive));
      }
    }
    return this.http.get<Agent[]>(this.baseUrl, { params });
  }

  getById(id: string): Observable<Agent> {
    return this.http.get<Agent>(`${this.baseUrl}/${id}`);
  }

  getDetails(id: string): Observable<AgentDetails> {
    return this.http.get<AgentDetails>(`${this.baseUrl}/${id}/details`);
  }

  getCalls(id: string, page = 1, pageSize = 10): Observable<AgentCallItem[]> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<AgentCallItem[]>(`${this.baseUrl}/${id}/calls`, { params });
  }

  create(request: CreateAgentRequest): Observable<Agent> {
    return this.http.post<Agent>(this.baseUrl, request);
  }

  update(id: string, request: UpdateAgentRequest): Observable<Agent> {
    return this.http.put<Agent>(`${this.baseUrl}/${id}`, request);
  }

  deactivate(id: string): Observable<Agent> {
    return this.http.put<Agent>(`${this.baseUrl}/${id}/deactivate`, null);
  }

  reactivate(id: string): Observable<Agent> {
    return this.http.put<Agent>(`${this.baseUrl}/${id}/reactivate`, null);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  updateStatus(id: string, status: AgentStatus): Observable<Agent> {
    return this.http.put<Agent>(`${this.baseUrl}/${id}/status`, { status });
  }
}
