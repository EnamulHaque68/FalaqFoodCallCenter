import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AgentService } from './agent.service';
import { environment } from '../../../environments/environment';
import { Agent, CreateAgentRequest, UpdateAgentRequest } from '../models/agent.models';
import { AgentStatus } from '../models/agent-dashboard.models';

describe('AgentService', () => {
  let service: AgentService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/agents`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AgentService]
    });

    service = TestBed.inject(AgentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getAll should request agents with query parameters', () => {
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
      }
    ];

    service.getAll({ search: 'Sarah', status: AgentStatus.Available, team: 'Support', isActive: true }).subscribe(agents => {
      expect(agents.length).toBe(1);
      expect(agents[0].employeeCode).toBe('AG001');
    });

    const req = httpMock.expectOne(request =>
      request.url === baseUrl &&
      request.params.get('search') === 'Sarah' &&
      request.params.get('status') === String(AgentStatus.Available) &&
      request.params.get('team') === 'Support' &&
      request.params.get('isActive') === 'true'
    );
    expect(req.request.method).toBe('GET');
    req.flush(mockAgents);
  });

  it('getById should request agent by id', () => {
    const mockAgent: Agent = {
      id: 'ag-1',
      userId: 'u-1',
      employeeCode: 'AG001',
      displayName: 'Sarah Connor',
      status: AgentStatus.Available,
      isActive: true,
      createdAt: new Date().toISOString()
    };

    service.getById('ag-1').subscribe(agent => {
      expect(agent).toEqual(mockAgent);
    });

    const req = httpMock.expectOne(`${baseUrl}/ag-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockAgent);
  });

  it('create should post create agent payload', () => {
    const requestDto: CreateAgentRequest = {
      employeeCode: 'AG002',
      displayName: 'John Connor',
      userName: 'john.connor',
      password: 'Password123!',
      team: 'Tier1',
      roleName: 'Agent'
    };

    const createdAgent: Agent = {
      id: 'ag-2',
      userId: 'u-2',
      employeeCode: 'AG002',
      displayName: 'John Connor',
      team: 'Tier1',
      status: AgentStatus.Offline,
      isActive: true,
      createdAt: new Date().toISOString()
    };

    service.create(requestDto).subscribe(agent => {
      expect(agent.employeeCode).toBe('AG002');
    });

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(requestDto);
    req.flush(createdAgent);
  });

  it('update should put updated agent payload', () => {
    const updateDto: UpdateAgentRequest = {
      employeeCode: 'AG002',
      displayName: 'John C. Connor',
      team: 'Escalations'
    };

    service.update('ag-2', updateDto).subscribe(agent => {
      expect(agent.displayName).toBe('John C. Connor');
    });

    const req = httpMock.expectOne(`${baseUrl}/ag-2`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(updateDto);
    req.flush({ id: 'ag-2', employeeCode: 'AG002', displayName: 'John C. Connor', team: 'Escalations', isActive: true });
  });

  it('updateStatus should put status change payload', () => {
    service.updateStatus('ag-1', AgentStatus.Busy).subscribe(agent => {
      expect(agent.status).toBe(AgentStatus.Busy);
    });

    const req = httpMock.expectOne(`${baseUrl}/ag-1/status`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ status: AgentStatus.Busy });
    req.flush({ id: 'ag-1', status: AgentStatus.Busy });
  });

  it('delete should send delete request', () => {
    service.delete('ag-1').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/ag-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
