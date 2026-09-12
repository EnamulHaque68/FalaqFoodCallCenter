import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { CustomerService } from './customer.service';
import { environment } from '../../../environments/environment';
import { CustomerPagedResult, Customer } from '../models/customer.models';

describe('CustomerService', () => {
  let service: CustomerService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/customers`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [CustomerService]
    });
    service = TestBed.inject(CustomerService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getPaged should send GET with search and paging params', () => {
    const mockResult: CustomerPagedResult = {
      items: [{ id: 'c1', fullName: 'Alice', phone: '8801711111111', createdAt: '' }],
      page: 1,
      pageSize: 10,
      totalCount: 1,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false
    };

    service.getPaged('Alice', 1, 10).subscribe((res) => {
      expect(res.items.length).toBe(1);
      expect(res.items[0].fullName).toBe('Alice');
    });

    const req = httpMock.expectOne((r) => r.url === baseUrl && r.params.get('search') === 'Alice' && r.params.get('page') === '1');
    expect(req.request.method).toBe('GET');
    req.flush(mockResult);
  });

  it('create should send POST with customer payload', () => {
    const mockCustomer: Customer = {
      id: 'c2',
      fullName: 'Bob',
      phone: '8801722222222',
      createdAt: ''
    };

    service.create({ fullName: 'Bob', phone: '8801722222222' }).subscribe((res) => {
      expect(res.id).toBe('c2');
      expect(res.fullName).toBe('Bob');
    });

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    req.flush(mockCustomer);
  });

  it('update should send PUT to customers/{id}', () => {
    const updated: Customer = {
      id: 'c2',
      fullName: 'Bob Updated',
      phone: '8801722222222',
      createdAt: ''
    };

    service.update('c2', { fullName: 'Bob Updated', phone: '8801722222222' }).subscribe((res) => {
      expect(res.fullName).toBe('Bob Updated');
    });

    const req = httpMock.expectOne(`${baseUrl}/c2`);
    expect(req.request.method).toBe('PUT');
    req.flush(updated);
  });

  it('delete should send DELETE to customers/{id}', () => {
    service.delete('c2').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/c2`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
