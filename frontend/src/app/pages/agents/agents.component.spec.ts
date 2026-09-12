import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AgentsComponent } from './agents.component';
import { AgentService } from '../../core/services/agent.service';
import { AuthService } from '../../core/auth/auth.service';
import { AgentStatus } from '../../core/models/agent-dashboard.models';
import { Agent } from '../../core/models/agent.models';

describe('AgentsComponent', () => {
  let component: AgentsComponent;
  let fixture: ComponentFixture<AgentsComponent>;
  let agentServiceMock: jasmine.SpyObj<AgentService>;
  let authServiceMock: jasmine.SpyObj<AuthService>;

  const mockAgents: Agent[] = [
    {
      id: 'ag-1',
      userId: 'u-1',
      employeeCode: 'AG001',
      displayName: 'Sarah Connor',
      team: 'Support',
      status: AgentStatus.Available,
      isActive: true,
      createdAt: new Date().toISOString()
    },
    {
      id: 'ag-2',
      userId: 'u-2',
      employeeCode: 'AG002',
      displayName: 'John Connor',
      team: 'Tier1',
      status: AgentStatus.Busy,
      isActive: true,
      createdAt: new Date().toISOString()
    },
    {
      id: 'ag-3',
      userId: 'u-3',
      employeeCode: 'AG003',
      displayName: 'Kyle Reese',
      team: 'Support',
      status: AgentStatus.Offline,
      isActive: false,
      createdAt: new Date().toISOString()
    }
  ];

  beforeEach(async () => {
    agentServiceMock = jasmine.createSpyObj('AgentService', [
      'getAll',
      'getById',
      'getDetails',
      'getCalls',
      'create',
      'update',
      'updateStatus',
      'deactivate',
      'reactivate',
      'delete'
    ]);

    agentServiceMock.getAll.and.returnValue(of(mockAgents));

    authServiceMock = jasmine.createSpyObj('AuthService', ['hasRole', 'hasPermission']);
    authServiceMock.hasRole.and.returnValue(true);

    await TestBed.configureTestingModule({
      imports: [AgentsComponent],
      providers: [
        { provide: AgentService, useValue: agentServiceMock },
        { provide: AuthService, useValue: authServiceMock }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AgentsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load agent list on init', () => {
    expect(component).toBeTruthy();
    expect(agentServiceMock.getAll).toHaveBeenCalled();
    expect(component.agents.length).toBe(3);
  });

  it('should calculate metrics correctly', () => {
    expect(component.totalCount).toBe(3);
    expect(component.availableCount).toBe(1);
    expect(component.busyCount).toBe(1);
    expect(component.offlineCount).toBe(1);
    expect(component.inactiveCount).toBe(1);
  });

  it('openCreateModal should reset and open modal', () => {
    component.openCreateModal();
    expect(component.showCreateModal).toBeTrue();
    expect(component.createForm.valid).toBeFalse();
    expect(component.createForm.controls.employeeCode.value).toBeFalsy();
    expect(component.createForm.controls.roleName.value).toBe('Agent');
  });

  it('createForm should validate required fields', () => {
    component.openCreateModal();
    const form = component.createForm;

    form.patchValue({
      employeeCode: 'AG100',
      displayName: 'Marcus Wright',
      team: 'Special Ops',
      userName: 'marcus.w',
      password: 'Password123!',
      roleName: 'Agent'
    });

    expect(form.valid).toBeTrue();
  });

  it('submitCreate should post payload and close modal on success', () => {
    component.openCreateModal();
    component.createForm.patchValue({
      employeeCode: 'AG100',
      displayName: 'Marcus Wright',
      team: 'Support',
      userName: 'marcus.w',
      password: 'Password123!',
      roleName: 'Agent'
    });

    const newAgent: Agent = {
      id: 'ag-4',
      userId: 'u-4',
      employeeCode: 'AG100',
      displayName: 'Marcus Wright',
      status: AgentStatus.Offline,
      isActive: true,
      createdAt: new Date().toISOString()
    };

    agentServiceMock.create.and.returnValue(of(newAgent));

    component.submitCreate();

    expect(agentServiceMock.create).toHaveBeenCalled();
    expect(component.showCreateModal).toBeFalse();
  });

  it('submitCreate should display error when creation fails', () => {
    component.openCreateModal();
    component.createForm.patchValue({
      employeeCode: 'AG001',
      displayName: 'Duplicate Agent',
      userName: 'dup.user',
      password: 'Password123!'
    });

    agentServiceMock.create.and.returnValue(
      throwError(() => ({ error: { message: 'Employee code already exists.' } }))
    );

    component.submitCreate();

    expect(component.modalError).toBe('Employee code already exists.');
    expect(component.showCreateModal).toBeTrue();
    expect(component.modalSaving).toBeFalse();
  });

  it('canManageAgents should return true for Admin/Supervisor', () => {
    authServiceMock.hasRole.and.returnValue(true);
    expect(component.canManageAgents()).toBeTrue();
  });
});
