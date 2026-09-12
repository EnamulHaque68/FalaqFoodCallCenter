import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { ActiveCallComponent } from './active-call.component';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';
import { IncomingCallSessionService } from '../../core/services/incoming-call-session.service';
import { TelephonyService } from '../../core/services/telephony.service';
import { TwilioVoiceService } from '../../core/services/twilio-voice.service';
import { CallService } from '../../core/services/call.service';
import { CustomerService } from '../../core/services/customer.service';
import { AgentService } from '../../core/services/agent.service';
import { QueueService } from '../../core/services/queue.service';
import { AgentDashboardService } from '../../core/services/agent-dashboard.service';
import { DispositionService } from '../../core/services/disposition.service';
import { CallStatus } from '../../core/models/agent-dashboard.models';

describe('ActiveCallComponent', () => {
  let component: ActiveCallComponent;
  let fixture: ComponentFixture<ActiveCallComponent>;
  let telephonyServiceMock: jasmine.SpyObj<TelephonyService>;
  let dispositionServiceMock: jasmine.SpyObj<DispositionService>;
  let callServiceMock: jasmine.SpyObj<CallService>;
  let customerServiceMock: jasmine.SpyObj<CustomerService>;
  let realtimeMock: any;
  let sessionServiceMock: any;
  let agentDashboardMock: any;

  beforeEach(async () => {
    telephonyServiceMock = jasmine.createSpyObj('TelephonyService', [
      'holdCall',
      'resumeCall',
      'transferCall',
      'completeCall'
    ]);

    dispositionServiceMock = jasmine.createSpyObj('DispositionService', ['getDispositions']);
    dispositionServiceMock.getDispositions.and.returnValue(
      of([
        { id: 'd1', code: 'SALE', name: 'Sale Made', requiresFollowUp: false, requiresNotes: false, sortOrder: 1, isActive: true, createdAt: new Date().toISOString() },
        { id: 'd2', code: 'FOLLOWUP', name: 'Follow Up Needed', requiresFollowUp: true, requiresNotes: true, sortOrder: 2, isActive: true, createdAt: new Date().toISOString() }
      ])
    );

    callServiceMock = jasmine.createSpyObj('CallService', ['getById', 'getTimeline']);
    callServiceMock.getById.and.returnValue(of({ id: 'test-call-id', notes: 'Test notes' } as any));
    callServiceMock.getTimeline.and.returnValue(of([]));

    customerServiceMock = jasmine.createSpyObj('CustomerService', ['getById', 'getCustomerCalls', 'getDetails']);
    customerServiceMock.getDetails.and.returnValue(of({ id: 'c1', fullName: 'Test Customer', phone: '01712345678' } as any));
    customerServiceMock.getCustomerCalls.and.returnValue(of({ items: [], totalCount: 0 } as any));

    const sessionMock: any = {
      event: {
        callId: 'test-call-id',
        customerId: 'c1',
        phoneNumber: '01712345678',
        direction: 'Inbound',
        status: 'Connected',
        startedAtUtc: new Date().toISOString()
      },
      customer: null
    };

    sessionServiceMock = {
      snapshot: sessionMock,
      session$: of(sessionMock),
      setSession: jasmine.createSpy('setSession'),
      clear: jasmine.createSpy('clear')
    };

    agentDashboardMock = {
      getMyDashboard: jasmine.createSpy('getMyDashboard').and.returnValue(of({
        agentId: 'ag1',
        agentName: 'Agent One',
        status: 'OnCall',
        currentCall: null
      }))
    };

    realtimeMock = {
      connect: jasmine.createSpy('connect'),
      events$: of(),
      incomingCalls$: of(null),
      callAssigned$: of(),
      notifications$: of(),
      clearIncomingCall: jasmine.createSpy('clearIncomingCall')
    };

    await TestBed.configureTestingModule({
      imports: [ActiveCallComponent],
      providers: [
        { provide: TelephonyService, useValue: telephonyServiceMock },
        { provide: DispositionService, useValue: dispositionServiceMock },
        { provide: CallService, useValue: callServiceMock },
        { provide: CustomerService, useValue: customerServiceMock },
        { provide: AgentService, useValue: jasmine.createSpyObj('AgentService', ['getAll']) },
        { provide: QueueService, useValue: jasmine.createSpyObj('QueueService', ['getQueues']) },
        { provide: AgentDashboardService, useValue: agentDashboardMock },
        { provide: IncomingCallSessionService, useValue: sessionServiceMock },
        { provide: CallCenterRealtimeService, useValue: realtimeMock },
        {
          provide: TwilioVoiceService,
          useValue: {
            initDevice: jasmine.createSpy('initDevice'),
            acceptCall: jasmine.createSpy('acceptCall'),
            rejectCall: jasmine.createSpy('rejectCall'),
            disconnectCall: jasmine.createSpy('disconnectCall'),
            toggleMute: jasmine.createSpy('toggleMute').and.returnValue(true),
            isMuted$: of(false),
            callState$: of('connected'),
            deviceState$: of('registered'),
            currentProvider$: of('Simulated')
          }
        },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: (k: string) => k === 'callId' ? 'test-call-id' : null } }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ActiveCallComponent);
    component = fixture.componentInstance;
    component.session = sessionMock;
    component.currentStatus = CallStatus.Connected;
    fixture.detectChanges();
  });

  it('should initialize with active connected state', () => {
    expect(component).toBeTruthy();
    expect(component.session?.event.callId).toBe('test-call-id');
    expect(component.isTerminal).toBeFalse();
  });

  it('openDispositionModal should load active dispositions', () => {
    component.openDispositionModal();
    expect(component.dispositionModalOpen).toBeTrue();
    expect(dispositionServiceMock.getDispositions).toHaveBeenCalledWith(false);
    expect(component.availableDispositions.length).toBe(2);
  });

  it('canSubmitDisposition should validate follow-up and notes rules', () => {
    // No disposition selected -> false
    expect(component.canSubmitDisposition).toBeFalse();

    // Standard disposition without followUp or notes -> true
    component.selectDisposition({
      id: 'd1',
      code: 'SALE',
      name: 'Sale Made',
      requiresFollowUp: false,
      requiresNotes: false,
      sortOrder: 1,
      isActive: true,
      createdAt: new Date().toISOString()
    });
    expect(component.canSubmitDisposition).toBeTrue();

    // Requires follow-up and notes
    component.selectDisposition({
      id: 'd2',
      code: 'FOLLOWUP',
      name: 'Follow Up Needed',
      requiresFollowUp: true,
      requiresNotes: true,
      sortOrder: 2,
      isActive: true,
      createdAt: new Date().toISOString()
    });
    expect(component.canSubmitDisposition).toBeFalse();

    // Populate future followUp and notes -> true
    const future = new Date(Date.now() + 86400000).toISOString().slice(0, 16);
    component.followUpDateTime = future;
    component.wrapUpNotes = 'Customer requested follow up tomorrow afternoon';
    expect(component.canSubmitDisposition).toBeTrue();
  });

  it('holdCall should invoke telephonyService.holdCall', () => {
    telephonyServiceMock.holdCall.and.returnValue(of({} as any));

    component.holdCall();

    expect(telephonyServiceMock.holdCall).toHaveBeenCalledWith('test-call-id');
  });

  it('resumeCall should invoke telephonyService.resumeCall', () => {
    telephonyServiceMock.resumeCall.and.returnValue(of({} as any));

    component.resumeCall();

    expect(telephonyServiceMock.resumeCall).toHaveBeenCalledWith('test-call-id');
  });

  it('toggleMute should toggle mute via voiceService', () => {
    component.toggleMute();
    expect(component.voiceService.toggleMute).toHaveBeenCalled();
    expect(component.isMuted).toBeTrue();
  });
});
