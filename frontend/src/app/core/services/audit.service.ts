import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLogItem, AuditLogPagedResult, AuditLogFilter } from '../models/audit.models';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/audit-logs`;

  getPaged(filter?: AuditLogFilter): Observable<AuditLogPagedResult> {
    let params = new HttpParams();

    if (filter) {
      if (filter.page) {
        params = params.set('page', filter.page.toString());
      }
      if (filter.pageSize) {
        params = params.set('pageSize', filter.pageSize.toString());
      }
      if (filter.search?.trim()) {
        params = params.set('search', filter.search.trim());
      }
      if (filter.action?.trim()) {
        params = params.set('action', filter.action.trim());
      }
      if (filter.entityName?.trim()) {
        params = params.set('entityName', filter.entityName.trim());
      }
      if (filter.userId?.trim()) {
        params = params.set('userId', filter.userId.trim());
      }
      if (filter.fromDate?.trim()) {
        params = params.set('fromUtc', filter.fromDate.trim());
      }
      if (filter.toDate?.trim()) {
        params = params.set('toUtc', filter.toDate.trim());
      }
    }

    return this.http.get<AuditLogPagedResult>(this.baseUrl, { params });
  }

  getById(id: string): Observable<AuditLogItem> {
    return this.http.get<AuditLogItem>(`${this.baseUrl}/${id}`);
  }

  getActions(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/actions`);
  }

  getEntities(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/entities`);
  }
}
