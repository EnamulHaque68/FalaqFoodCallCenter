import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError, Subject } from 'rxjs';
import { AgentDashboardComponent } from './agent-dashboard.component';
import { AgentDashboardService } from '../../core/services/agent-dashboard.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { AgentDashboard, AgentStatus, CallDirection, CallStatus } from '../../core/models/agent-dashboard.models';

describe('AgentDashboardComponent', () => {
  let component: AgentDashboardComponent;
  let fixture: ComponentFixture<AgentDashboardComponent>;
  let dashboardServiceMock: jasmine.SpyObj<AgentDashboardService>;
  let realtimeMock: any;
  let routerMock: jasmine.SpyObj<Router>;
  let realtimeEvents$: Subject<any>;

  const mockDashboard: AgentDashboard = {
    agentId: 'ag-1',
    userId: 'u-1',
    displayName: 'Sarah Connor',
    employeeCode: 'AG001',
    team: 'Tier 1 Support',
    status: AgentStatus.Available,
    todaysCallsCount: 10,
    completedCallsCount: 8,
    missedCallsCount: 2,
    averageDurationSeconds: 150,
    queueCount: 0,
    currentCall: null,
    incomingQueue: [],
    recentCalls: []
  };

  beforeEach(async () => {
    realtimeEvents$ = new Subject();
    realtimeMock = {
      connect: jasmine.createSpy('connect'),
      events$: realtimeEvents$.asObservable(),
      incomingCalls$: of(null)
    };

    dashboardServiceMock = jasmine.createSpyObj('AgentDashboardService', ['getMyDashboard', 'updateMyStatus']);
    dashboardServiceMock.getMyDashboard.and.returnValue(of(mockDashboard));

    routerMock = jasmine.createSpyObj('Router', ['navigate']);

    await TestBed.configureTestingModule({
      imports: [AgentDashboardComponent],
      providers: [
        { provide: AgentDashboardService, useValue: dashboardServiceMock },
        { provide: CallCenterRealtimeService, useValue: realtimeMock },
        { provide: Router, useValue: routerMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AgentDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load dashboard on init', () => {
    expect(component).toBeTruthy();
    expect(realtimeMock.connect).toHaveBeenCalled();
    expect(dashboardServiceMock.getMyDashboard).toHaveBeenCalled();
    expect(component.dashboard).toEqual(mockDashboard);
    expect(component.dashboard?.status).toBe(AgentStatus.Available);
  });

  it('should update status when changeStatus is called', () => {
    dashboardServiceMock.updateMyStatus.and.returnValue(of({
      id: 'ag-1',
      status: AgentStatus.Busy
    } as any));

    component.changeStatus(AgentStatus.Busy);

    expect(dashboardServiceMock.updateMyStatus).toHaveBeenCalledWith('ag-1', AgentStatus.Busy);
    expect(component.dashboard?.status).toBe(AgentStatus.Busy);
  });

  it('should handle error when status update fails', () => {
    dashboardServiceMock.updateMyStatus.and.returnValue(
      throwError(() => ({ error: { message: 'Network failure' } }))
    );

    component.changeStatus(AgentStatus.Away);

    expect(component.errorMessage).toBe('Network failure');
    expect(component.statusUpdating).toBeFalse();
  });

  it('should reload dashboard on incoming real-time call-status event', () => {
    dashboardServiceMock.getMyDashboard.calls.reset();
    dashboardServiceMock.getMyDashboard.and.returnValue(of(mockDashboard));

    realtimeEvents$.next({ type: 'call-status' });

    expect(dashboardServiceMock.getMyDashboard).toHaveBeenCalled();
  });

  it('should calculate active call duration when currentCall is present', () => {
    const tenSecondsAgo = new Date(Date.now() - 10000).toISOString();
    component.dashboard = {
      ...mockDashboard,
      currentCall: {
        id: 'call-1',
        customerId: 'cust-1',
        correlationId: 'corr-1',
        phoneNumber: '01712345678',
        direction: CallDirection.Inbound,
        status: CallStatus.Connected,
        startedAt: tenSecondsAgo,
        createdAt: tenSecondsAgo,
        customerName: 'Customer A'
      }
    };

    (component as any).updateActiveCallTimer();

    expect(component.activeCallDurationFormatted).toMatch(/^00:\d{2}$/);
  });
});
