import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { CustomersComponent } from './customers.component';
import { CustomerService } from '../../core/services/customer.service';
import { CallService } from '../../core/services/call.service';
import { IncomingCallSessionService } from '../../core/services/incoming-call-session.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { AuthService } from '../../core/auth/auth.service';

describe('CustomersComponent', () => {
  let component: CustomersComponent;
  let fixture: ComponentFixture<CustomersComponent>;
  let customerServiceMock: jasmine.SpyObj<CustomerService>;
  let authServiceMock: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    customerServiceMock = jasmine.createSpyObj<CustomerService>('CustomerService', [
      'getPaged',
      'create',
      'update',
      'delete',
      'getDetails',
      'getCustomerCalls',
      'lookupByPhone'
    ]);

    customerServiceMock.getPaged.and.returnValue(
      of({
        items: [
          { id: 'c1', fullName: 'Ayesha Rahman', phone: '01711111111', createdAt: new Date().toISOString() }
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false
      })
    );

    authServiceMock = jasmine.createSpyObj<AuthService>('AuthService', ['hasPermission', 'hasRole']);
    authServiceMock.hasPermission.and.returnValue(true);
    authServiceMock.hasRole.and.returnValue(true);

    await TestBed.configureTestingModule({
      imports: [CustomersComponent],
      providers: [
        { provide: CustomerService, useValue: customerServiceMock },
        { provide: CallService, useValue: jasmine.createSpyObj('CallService', ['createOutgoing']) },
        { provide: IncomingCallSessionService, useValue: jasmine.createSpyObj('IncomingCallSessionService', ['setSession']) },
        { provide: CallCenterRealtimeService, useValue: { onCallUpdated$: of(null) } },
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should initialize and load customer list', () => {
    component.loadPage(1);
    expect(component).toBeTruthy();
    expect(customerServiceMock.getPaged).toHaveBeenCalled();
    expect(component.result.items.length).toBe(1);
    expect(component.result.items[0].fullName).toBe('Ayesha Rahman');
  });

  it('openCreateModal should open modal and reset form', () => {
    component.openCreateModal();
    expect(component.showFormModal).toBeTrue();
    expect(component.isEditing).toBeFalse();
    expect(component.customerForm.valid).toBeFalse();
  });

  it('submitForm should call customerService.create when valid', () => {
    component.openCreateModal();
    component.customerForm.setValue({
      fullName: 'New Customer',
      phone: '01722222222',
      email: 'new@example.com',
      address: 'Dhaka',
      crmCustomerId: 'CRM-123',
      notes: 'New note'
    });

    customerServiceMock.create.and.returnValue(
      of({ id: 'c2', fullName: 'New Customer', phone: '01722222222', createdAt: new Date().toISOString() })
    );

    component.submitForm();

    expect(customerServiceMock.create).toHaveBeenCalled();
    expect(component.showFormModal).toBeFalse();
  });
});
