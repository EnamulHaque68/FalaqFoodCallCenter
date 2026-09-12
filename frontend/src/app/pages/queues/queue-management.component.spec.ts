import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { QueueManagementComponent } from './queue-management.component';
import { QueueService } from '../../core/services/queue.service';
import { AgentService } from '../../core/services/agent.service';
import { CallCenterRealtimeService } from '../../core/services/call-center-realtime.service';

describe('QueueManagementComponent', () => {
  let component: QueueManagementComponent;
  let fixture: ComponentFixture<QueueManagementComponent>;
  let queueServiceMock: jasmine.SpyObj<QueueService>;

  beforeEach(async () => {
    queueServiceMock = jasmine.createSpyObj<QueueService>('QueueService', [
      'getQueues',
      'getSummary',
      'getQueueEntries',
      'triggerAutoAssign',
      'createQueue',
      'updateQueue',
      'deleteQueue'
    ]);

    queueServiceMock.getQueues.and.returnValue(
      of([
        {
          id: 'q1',
          name: 'Support Queue',
          priority: 1,
          isActive: true,
          waitingCallsCount: 2,
          averageWaitSeconds: 30,
          longestWaitSeconds: 60,
          createdAt: ''
        }
      ])
    );

    queueServiceMock.getSummary.and.returnValue(
      of({
        totalQueues: 1,
        activeQueues: 1,
        totalWaitingCalls: 2,
        averageWaitSeconds: 30,
        longestWaitSeconds: 60,
        availableAgentsCount: 5
      })
    );

    queueServiceMock.getQueueEntries.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [QueueManagementComponent],
      providers: [
        { provide: QueueService, useValue: queueServiceMock },
        { provide: AgentService, useValue: { getAll: () => of([]) } },
        {
          provide: CallCenterRealtimeService,
          useValue: {
            connect: jasmine.createSpy('connect'),
            events$: of(),
            onCallEnqueued$: of(null),
            onCallDequeued$: of(null),
            onQueueUpdated$: of(null)
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(QueueManagementComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should initialize and load queue data and summary', () => {
    expect(component).toBeTruthy();
    expect(queueServiceMock.getQueues).toHaveBeenCalled();
    expect(queueServiceMock.getSummary).toHaveBeenCalled();
    expect(component.queues().length).toBe(1);
    expect(component.queues()[0].name).toBe('Support Queue');
  });

  it('triggerAutoAssign should call queueService.triggerAutoAssign', () => {
    queueServiceMock.triggerAutoAssign.and.returnValue(of({}));

    component.triggerAutoAssign();

    expect(queueServiceMock.triggerAutoAssign).toHaveBeenCalled();
  });

  it('openCreateModal should initialize modal state', () => {
    component.openCreateModal();
    expect(component.showQueueModal()).toBeTrue();
    expect(component.queueForm.name).toBe('');
  });
});
