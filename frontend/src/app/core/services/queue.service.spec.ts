import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { QueueService } from './queue.service';
import { environment } from '../../../environments/environment';
import { CallQueue, QueueSummary } from '../models/queue.models';

describe('QueueService', () => {
  let service: QueueService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/queues`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [QueueService]
    });
    service = TestBed.inject(QueueService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getQueues should return queue list', () => {
    const mockQueues: CallQueue[] = [
      { id: 'q1', name: 'Support', priority: 1, isActive: true, waitingCallsCount: 2, averageWaitSeconds: 30, longestWaitSeconds: 60, createdAt: '' }
    ];

    service.getQueues().subscribe((queues) => {
      expect(queues.length).toBe(1);
      expect(queues[0].name).toBe('Support');
    });

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    req.flush(mockQueues);
  });

  it('getSummary should return queue summary metrics', () => {
    const mockSummary: QueueSummary = {
      totalQueues: 3,
      activeQueues: 3,
      totalWaitingCalls: 5,
      averageWaitSeconds: 45,
      longestWaitSeconds: 120,
      availableAgentsCount: 5
    };

    service.getSummary().subscribe((summary) => {
      expect(summary.totalQueues).toBe(3);
      expect(summary.totalWaitingCalls).toBe(5);
    });

    const req = httpMock.expectOne(`${baseUrl}/summary`);
    expect(req.request.method).toBe('GET');
    req.flush(mockSummary);
  });

  it('prioritizeEntry should post new priority', () => {
    service.prioritizeEntry('e1', 10).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/entries/e1/priority`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ priority: 10 });
    req.flush({});
  });

  it('cancelCall should post to calls/{id}/cancel with reason param', () => {
    service.cancelCall('c1', 'Caller disconnected').subscribe();

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/calls/c1/cancel` && r.params.get('reason') === 'Caller disconnected');
    expect(req.request.method).toBe('POST');
    req.flush({ message: 'Cancelled' });
  });
});
