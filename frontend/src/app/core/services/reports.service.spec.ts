import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ReportsService } from './reports.service';
import { environment } from '../../../environments/environment';
import { ReportMetrics } from '../models/call-history.models';

describe('ReportsService', () => {
  let service: ReportsService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/reports`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ReportsService]
    });
    service = TestBed.inject(ReportsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getMetrics should fetch report metrics', () => {
    const mockMetrics: ReportMetrics = {
      totalCalls: 50,
      incoming: 40,
      outgoing: 10,
      completed: 45,
      missed: 3,
      rejected: 2,
      averageDurationSeconds: 120,
      availableAgents: 5,
      busyAgents: 2,
      awayAgents: 1,
      offlineAgents: 2,
      totalAgents: 10,
      activeCallsCount: 2,
      queueSize: 1,
      disposedCallsCount: 45,
      pendingFollowUpsCount: 3
    };

    service.getMetrics().subscribe((metrics) => {
      expect(metrics.totalCalls).toBe(50);
      expect(metrics.averageDurationSeconds).toBe(120);
    });

    const req = httpMock.expectOne(`${baseUrl}/metrics`);
    expect(req.request.method).toBe('GET');
    req.flush(mockMetrics);
  });

  it('getAnalytics should pass preset and date filters', () => {
    service.getAnalytics('today', '2026-09-10T00:00:00Z', '2026-09-10T23:59:59Z').subscribe();

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/analytics` && r.params.get('preset') === 'today');
    expect(req.request.method).toBe('GET');
    req.flush({});
  });
});
