import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ComprehensiveAnalyticsReport, DispositionReport, OperationsDashboard, ReportMetrics } from '../models/call-history.models';

@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);

  getMetrics(): Observable<ReportMetrics> {
    return this.http.get<ReportMetrics>(`${environment.apiBaseUrl}/reports/metrics`);
  }

  getDashboard(): Observable<OperationsDashboard> {
    return this.http.get<OperationsDashboard>(`${environment.apiBaseUrl}/reports/dashboard`);
  }

  getDispositionReport(fromUtc?: string, toUtc?: string): Observable<DispositionReport> {
    let params = new HttpParams();
    if (fromUtc) params = params.set('fromUtc', fromUtc);
    if (toUtc) params = params.set('toUtc', toUtc);
    return this.http.get<DispositionReport>(`${environment.apiBaseUrl}/reports/dispositions`, { params });
  }

  getAnalytics(preset?: string, fromUtc?: string, toUtc?: string): Observable<ComprehensiveAnalyticsReport> {
    let params = new HttpParams();
    if (preset) params = params.set('preset', preset);
    if (fromUtc) params = params.set('fromUtc', fromUtc);
    if (toUtc) params = params.set('toUtc', toUtc);
    return this.http.get<ComprehensiveAnalyticsReport>(`${environment.apiBaseUrl}/reports/analytics`, { params });
  }

  downloadExportCsv(preset?: string, fromUtc?: string, toUtc?: string): Observable<Blob> {
    let params = new HttpParams();
    if (preset) params = params.set('preset', preset);
    if (fromUtc) params = params.set('fromUtc', fromUtc);
    if (toUtc) params = params.set('toUtc', toUtc);
    return this.http.get(`${environment.apiBaseUrl}/reports/export/calls`, {
      params,
      responseType: 'blob'
    });
  }
}

