import { CallDirection, CallStatus } from './agent-dashboard.models';

export interface CallHistoryItem {
  id: string;
  customerId: string;
  customerName: string;
  customerPhone: string;
  assignedAgentId?: string | null;
  agentName?: string | null;
  callDispositionId?: string | null;
  dispositionName?: string | null;
  direction: CallDirection;
  status: CallStatus;
  phoneNumber: string;
  notes?: string | null;
  followUpAt?: string | null;
  followUpNotes?: string | null;
  startedAt: string;
  answeredAt?: string | null;
  endedAt?: string | null;
  durationSeconds?: number | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CallTimelineEvent {
  id: string;
  callId: string;
  eventType: string;
  description: string;
  metadataJson?: string | null;
  agentId?: string | null;
  agentName?: string | null;
  occurredAt: string;
}

export interface CallHistoryPagedResult {
  items: CallHistoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CallHistoryQuery {
  search?: string;
  customerId?: string;
  agentId?: string;
  direction?: CallDirection;
  status?: CallStatus;
  fromUtc?: string | null;
  toUtc?: string | null;
  dispositionId?: string | null;
  page: number;
  pageSize: number;
}

export interface ReportMetrics {
  totalCalls: number;
  incoming: number;
  outgoing: number;
  completed: number;
  missed: number;
  rejected: number;
  averageDurationSeconds: number;
  availableAgents: number;
  busyAgents: number;
  awayAgents: number;
  offlineAgents: number;
  totalAgents: number;
  activeCallsCount: number;
  queueSize: number;
  disposedCallsCount?: number;
  pendingFollowUpsCount?: number;
}

export interface PeriodStats {
  periodName: string;
  totalCalls: number;
  incoming: number;
  outgoing: number;
  completed: number;
  missed: number;
  rejected: number;
  averageDurationSeconds: number;
  completionRatePercent: number;
}

export interface CallTrendPoint {
  date: string;
  periodLabel: string;
  totalCalls: number;
  completedCalls: number;
  missedCalls: number;
  rejectedCalls: number;
  incomingCalls: number;
  outgoingCalls: number;
}

export interface AgentStatusItem {
  id: string;
  displayName: string;
  employeeCode: string;
  team?: string | null;
  status: number;
  isActive: boolean;
  currentCallPhoneNumber?: string | null;
  currentCallStartedAt?: string | null;
}

export interface AgentStatusSummary {
  available: number;
  busy: number;
  away: number;
  offline: number;
  total: number;
  agents: AgentStatusItem[];
}

export interface QueueStatusItem {
  callId: string;
  phoneNumber: string;
  position: number;
  enqueuedAt: string;
  queueName?: string | null;
}

export interface QueueStatusSummary {
  totalWaiting: number;
  averageWaitSeconds: number;
  longestWaitSeconds: number;
  entries: QueueStatusItem[];
}

export interface OperationsDashboard {
  metrics: ReportMetrics;
  dailyStats: PeriodStats;
  weeklyStats: PeriodStats;
  monthlyStats: PeriodStats;
  callTrends: CallTrendPoint[];
  agentStatus: AgentStatusSummary;
  queueStatus: QueueStatusSummary;
  dispositionBreakdown?: DispositionBreakdown[];
  pendingFollowUpsCount?: number;
  generatedAt: string;
}

export interface DispositionBreakdown {
  dispositionId: string;
  dispositionName: string;
  dispositionCode: string;
  callCount: number;
  percentage: number;
  requiresFollowUp: boolean;
  requiresNotes: boolean;
}

export interface PendingFollowUpCall {
  callId: string;
  customerId: string;
  customerName: string;
  phoneNumber: string;
  agentId?: string | null;
  agentName?: string | null;
  dispositionName: string;
  followUpAt: string;
  followUpNotes?: string | null;
  callEndedAt?: string | null;
}

export interface DispositionReport {
  fromUtc?: string | null;
  toUtc?: string | null;
  totalCompletedCalls: number;
  totalDisposedCalls: number;
  followUpsScheduledCount: number;
  breakdowns: DispositionBreakdown[];
  upcomingFollowUps: PendingFollowUpCall[];
}

export interface CallVolumeReport {
  totalCalls: number;
  incoming: number;
  outgoing: number;
  completed: number;
  missed: number;
  rejected: number;
  averageDurationSeconds: number;
  totalTalkTimeSeconds: number;
}

export interface AgentPerformanceReport {
  agentId: string;
  agentName: string;
  employeeCode: string;
  team?: string | null;
  totalCalls: number;
  completedCalls: number;
  missedCalls: number;
  rejectedCalls: number;
  totalTalkTimeSeconds: number;
  averageTalkTimeSeconds: number;
  completionRatePercent: number;
}

export interface QueuePerformanceReport {
  queueId: string;
  queueName: string;
  totalEnqueued: number;
  answeredCalls: number;
  abandonedCalls: number;
  averageWaitSeconds: number;
  maxWaitSeconds: number;
  currentWaiting: number;
}

export interface DispositionStat {
  dispositionId: string;
  dispositionCode: string;
  dispositionName: string;
  callCount: number;
  percentage: number;
  totalTalkTimeSeconds: number;
  averageDurationSeconds: number;
  requiresFollowUp: boolean;
}

export interface CallTimeSeriesPoint {
  timestamp: string;
  periodLabel: string;
  total: number;
  incoming: number;
  outgoing: number;
  completed: number;
  missed: number;
  rejected: number;
}

export interface DistributionItem {
  label: string;
  key: string;
  count: number;
  percentage: number;
}

export interface AgentCallCount {
  agentId: string;
  agentName: string;
  callCount: number;
  completedCount: number;
  talkTimeSeconds: number;
}

export interface AnalyticsCharts {
  callsOverTime: CallTimeSeriesPoint[];
  callsByAgent: AgentCallCount[];
  callsByDirection: DistributionItem[];
  callsByStatus: DistributionItem[];
  callsByDisposition: DistributionItem[];
}

export interface ComprehensiveAnalyticsReport {
  periodPreset: string;
  fromUtc: string;
  toUtc: string;
  volume: CallVolumeReport;
  agents: AgentPerformanceReport[];
  queues: QueuePerformanceReport[];
  dispositions: DispositionStat[];
  charts: AnalyticsCharts;
  generatedAt: string;
}


