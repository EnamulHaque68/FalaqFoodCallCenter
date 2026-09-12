import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DispositionService } from './disposition.service';
import { environment } from '../../../environments/environment';
import { CallDisposition } from '../models/disposition.models';

describe('DispositionService', () => {
  let service: DispositionService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/dispositions`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DispositionService]
    });
    service = TestBed.inject(DispositionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getDispositions should send includeInactive param', () => {
    const mockList: CallDisposition[] = [
      { id: 'd1', code: 'SALE', name: 'Sale Closed', requiresFollowUp: false, requiresNotes: false, sortOrder: 1, isActive: true, createdAt: new Date().toISOString() }
    ];

    service.getDispositions(true).subscribe((list) => {
      expect(list.length).toBe(1);
      expect(list[0].code).toBe('SALE');
    });

    const req = httpMock.expectOne(`${baseUrl}?includeInactive=true`);
    expect(req.request.method).toBe('GET');
    req.flush(mockList);
  });

  it('create should post new disposition', () => {
    const newDisp: CallDisposition = {
      id: 'd2',
      code: 'REFUND',
      name: 'Refund Requested',
      requiresFollowUp: true,
      requiresNotes: true,
      sortOrder: 2,
      isActive: true,
      createdAt: new Date().toISOString()
    };

    service.create({ code: 'REFUND', name: 'Refund Requested', requiresFollowUp: true, requiresNotes: true, sortOrder: 2 }).subscribe((disp) => {
      expect(disp.code).toBe('REFUND');
    });

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    req.flush(newDisp);
  });
});
