import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ReportsComponent } from './reports.component';
import { ReportsService } from '../../core/services/reports.service';
import { DispositionService } from '../../core/services/disposition.service';
import { AuthService } from '../../core/auth/auth.service';
import { ComprehensiveAnalyticsReport } from '../../core/models/call-history.models';

describe('ReportsComponent', () => {
  let component: ReportsComponent;
  let fixture: ComponentFixture<ReportsComponent>;
  let reportsServiceMock: jasmine.SpyObj<ReportsService>;
  let dispositionServiceMock: jasmine.SpyObj<DispositionService>;

  const mockAnalytics: ComprehensiveAnalyticsReport = {
    periodPreset: 'today',
    fromUtc: new Date().toISOString(),
    toUtc: new Date().toISOString(),
    volume: {
      totalCalls: 100,
      incoming: 80,
      outgoing: 20,
      completed: 90,
      missed: 5,
      rejected: 5,
      averageDurationSeconds: 150,
      totalTalkTimeSeconds: 15000
    },
    agents: [],
    queues: [],
    dispositions: [],
    charts: {
      callsOverTime: [],
      callsByAgent: [],
      callsByDirection: [],
      callsByStatus: [],
      callsByDisposition: []
    },
    generatedAt: new Date().toISOString()
  };

  beforeEach(async () => {
    reportsServiceMock = jasmine.createSpyObj<ReportsService>('ReportsService', [
      'getAnalytics',
      'getMetrics',
      'downloadExportCsv'
    ]);

    reportsServiceMock.getAnalytics.and.returnValue(of(mockAnalytics));

    dispositionServiceMock = jasmine.createSpyObj<DispositionService>('DispositionService', [
      'getDispositions',
      'create',
      'update',
      'toggleStatus'
    ]);
    dispositionServiceMock.getDispositions.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [ReportsComponent],
      providers: [
        { provide: ReportsService, useValue: reportsServiceMock },
        { provide: DispositionService, useValue: dispositionServiceMock },
        {
          provide: AuthService,
          useValue: {
            userRole: () => 'Admin',
            hasPermission: () => true,
            hasRole: () => true
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should initialize and load analytics report', () => {
    expect(component).toBeTruthy();
    expect(reportsServiceMock.getAnalytics).toHaveBeenCalled();
    expect(component.analytics).not.toBeNull();
    expect(component.analytics?.volume.totalCalls).toBe(100);
  });

  it('setActiveTab should switch between analytics and config tabs', () => {
    expect(component.activeTab).toBe('analytics');

    component.setActiveTab('config');
    expect(component.activeTab).toBe('config');
    expect(dispositionServiceMock.getDispositions).toHaveBeenCalledWith(true);

    component.setActiveTab('analytics');
    expect(component.activeTab).toBe('analytics');
  });

  it('exportCsv should trigger downloadExportCsv', () => {
    reportsServiceMock.downloadExportCsv.and.returnValue(of(new Blob(['csv,data'], { type: 'text/csv' })));

    component.exportCsv();

    expect(reportsServiceMock.downloadExportCsv).toHaveBeenCalled();
  });
});
