export enum AgentStatus {
  Offline = 1,
  Available = 2,
  Busy = 3,
  WrapUp = 4,
  Away = 5
}

export enum CallDirection {
  Inbound = 1,
  Outbound = 2
}

export enum CallStatus {
  Queued = 1,
  Ringing = 2,
  Connected = 3,
  Completed = 4,
  Abandoned = 5,
  Rejected = 6,
  Failed = 7,
  OnHold = 8
}

export interface AgentCall {
  id: string;
  customerId: string;
  customerName?: string | null;
  customerPhone?: string | null;
  assignedAgentId?: string | null;
  agentName?: string | null;
  callQueueId?: string | null;
  callDispositionId?: string | null;
  dispositionName?: string | null;
  providerCallId?: string | null;
  correlationId: string;
  direction: CallDirection;
  status: CallStatus;
  phoneNumber: string;
  notes?: string | null;
  startedAt: string;
  answeredAt?: string | null;
  endedAt?: string | null;
  durationSeconds?: number | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface QueueItemSummary {
  callId: string;
  phoneNumber: string;
  position: number;
  enqueuedAt: string;
  queueName?: string | null;
}

export interface AgentDashboard {
  agentId: string;
  userId: string;
  employeeCode: string;
  displayName: string;
  team?: string | null;
  status: AgentStatus;
  todaysCallsCount: number;
  completedCallsCount: number;
  missedCallsCount: number;
  averageDurationSeconds: number;
  currentCall?: AgentCall | null;
  queueCount: number;
  incomingQueue: QueueItemSummary[];
  recentCalls: AgentCall[];
}
