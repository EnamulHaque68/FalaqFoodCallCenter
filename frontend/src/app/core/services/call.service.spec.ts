import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { CallService, CallResponse } from './call.service';
import { environment } from '../../../environments/environment';

describe('CallService', () => {
  let service: CallService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/calls`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [CallService]
    });
    service = TestBed.inject(CallService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getById should fetch call by id', () => {
    const mockCall: CallResponse = {
      id: 'call-1',
      customerId: 'cust-1',
      correlationId: 'corr-1',
      direction: 'Inbound',
      status: 'Connected',
      phoneNumber: '8801712345678',
      startedAt: new Date().toISOString(),
      createdAt: new Date().toISOString()
    };

    service.getById('call-1').subscribe((call) => {
      expect(call.id).toBe('call-1');
      expect(call.status).toBe('Connected');
    });

    const req = httpMock.expectOne(`${baseUrl}/call-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockCall);
  });

  it('complete should post disposition and notes to complete endpoint', () => {
    service.complete('call-1', 'disp-1', 'Completed successfully').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/call-1/complete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ dispositionId: 'disp-1', notes: 'Completed successfully' });
    req.flush({});
  });

  it('updateNotes should put notes to notes endpoint', () => {
    service.updateNotes('call-1', 'Order inquiry resolved').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/call-1/notes`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ notes: 'Order inquiry resolved' });
    req.flush({});
  });
});
