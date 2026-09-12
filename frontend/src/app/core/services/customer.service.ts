import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Customer,
  CustomerPagedResult,
  CreateCustomerRequest,
  UpdateCustomerRequest,
  CustomerCallsPagedResult
} from '../models/customer.models';
import { CustomerSummary } from '../models/incoming-call.models';

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/customers`;

  getPaged(search: string, page: number, pageSize: number): Observable<CustomerPagedResult> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    const term = search.trim();
    if (term) params = params.set('search', term);
    return this.http.get<CustomerPagedResult>(this.baseUrl, { params });
  }

  getById(customerId: string): Observable<CustomerSummary> {
    return this.http.get<CustomerSummary>(`${this.baseUrl}/${customerId}`);
  }

  getDetails(customerId: string): Observable<Customer> {
    return this.http.get<Customer>(`${this.baseUrl}/${customerId}`);
  }

  create(request: CreateCustomerRequest): Observable<Customer> {
    return this.http.post<Customer>(this.baseUrl, request);
  }

  update(id: string, request: UpdateCustomerRequest): Observable<Customer> {
    return this.http.put<Customer>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  lookupByPhone(phone: string): Observable<Customer | null> {
    const params = new HttpParams().set('phone', phone.trim());
    return this.http.get<Customer | null>(`${this.baseUrl}/lookup/phone`, { params });
  }

  getCustomerCalls(customerId: string, page = 1, pageSize = 10): Observable<CustomerCallsPagedResult> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<CustomerCallsPagedResult>(`${this.baseUrl}/${customerId}/calls`, { params });
  }
}
