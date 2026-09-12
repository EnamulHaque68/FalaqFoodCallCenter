import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TelephonyService } from './telephony.service';
import { environment } from '../../../environments/environment';
import { TelephonyCallResponse, TransferType } from '../models/incoming-call.models';
import { CallDirection, CallStatus } from '../models/agent-dashboard.models';
import { CompleteCallRequest } from '../models/disposition.models';

describe('TelephonyService', () => {
  let service: TelephonyService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/telephony`;

  const mockResponse: TelephonyCallResponse = {
    callId: 'call-100',
    providerCallId: 'prov-100',
    direction: CallDirection.Inbound,
    status: CallStatus.Connected,
    phoneNumber: '01712345678',
    correlationId: 'corr-100'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [TelephonyService]
    });

    service = TestBed.inject(TelephonyService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('acceptCall should post accept action', () => {
    service.acceptCall('call-100').subscribe(res => {
      expect(res.callId).toBe('call-100');
    });

    const req = httpMock.expectOne(`${baseUrl}/calls/call-100/accept`);
    expect(req.request.method).toBe('POST');
    req.flush(mockResponse);
  });

  it('rejectCall should post reject action', () => {
    service.rejectCall('call-100').subscribe(res => {
      expect(res.callId).toBe('call-100');
      expect(res.status).toBe(CallStatus.Rejected);
    });

    const req = httpMock.expectOne(`${baseUrl}/calls/call-100/reject`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockResponse, status: CallStatus.Rejected });
  });

  it('holdCall should post hold action', () => {
    service.holdCall('call-100').subscribe(res => {
      expect(res.status).toBe(CallStatus.OnHold);
    });

    const req = httpMock.expectOne(`${baseUrl}/calls/call-100/hold`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockResponse, status: CallStatus.OnHold });
  });

  it('resumeCall should post resume action', () => {
    service.resumeCall('call-100').subscribe(res => {
      expect(res.status).toBe(CallStatus.Connected);
    });

    const req = httpMock.expectOne(`${baseUrl}/calls/call-100/resume`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockResponse, status: CallStatus.Connected });
  });

  it('transferCall should post transfer request', () => {
    const transferReq = {
      targetAgentId: 'ag-2',
      transferType: TransferType.Blind
    };

    service.transferCall('call-100', transferReq).subscribe(res => {
      expect(res.status).toBe(CallStatus.Ringing);
    });

    const req = httpMock.expectOne(`${baseUrl}/calls/call-100/transfer`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(transferReq);
    req.flush({ ...mockResponse, status: CallStatus.Ringing });
  });

  it('completeCall should post disposition completion payload', () => {
    const completeReq: CompleteCallRequest = {
      dispositionId: 'disp-1',
      notes: 'Customer order confirmed',
      followUpAt: new Date().toISOString()
    };

    service.completeCall('call-100', completeReq).subscribe(res => {
      expect(res.status).toBe(CallStatus.Completed);
    });

    const req = httpMock.expectOne(`${baseUrl}/calls/call-100/complete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(completeReq);
    req.flush({ ...mockResponse, status: CallStatus.Completed });
  });

  it('simulateIncoming should post simulate payload', () => {
    service.simulateIncoming({ phoneNumber: '01711111111' }).subscribe(res => {
      expect(res.callId).toBe('call-100');
    });

    const req = httpMock.expectOne(`${baseUrl}/incoming/simulate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.phoneNumber).toBe('01711111111');
    req.flush(mockResponse);
  });
});
