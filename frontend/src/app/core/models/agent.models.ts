import { AgentStatus, CallDirection, CallStatus } from './agent-dashboard.models';

export interface Agent {
  id: string;
  userId: string;
  employeeCode: string;
  displayName: string;
  team?: string | null;
  isActive: boolean;
  roleName?: string | null;
  userName?: string | null;
  status: AgentStatus;
  createdAt: string;
  updatedAt?: string | null;
}

// Keep backwards-compatible alias
export type AgentResponse = Agent;

export interface CreateAgentRequest {
  employeeCode: string;
  displayName: string;
  team?: string | null;
  userName?: string | null;
  password?: string | null;
  roleName?: string | null;
  userId?: string | null;
}

export interface UpdateAgentRequest {
  employeeCode: string;
  displayName: string;
  team?: string | null;
  isActive?: boolean | null;
  roleName?: string | null;
  password?: string | null;
}

export interface AgentCallItem {
  id: string;
  customerId: string;
  assignedAgentId?: string | null;
  callQueueId?: string | null;
  callDispositionId?: string | null;
  providerCallId?: string | null;
  correlationId: string;
  direction: CallDirection;
  status: CallStatus;
  phoneNumber: string;
  startedAt: string;
  answeredAt?: string | null;
  endedAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface AgentDetails {
  id: string;
  userId: string;
  employeeCode: string;
  displayName: string;
  team?: string | null;
  isActive: boolean;
  roleName?: string | null;
  userName?: string | null;
  status: AgentStatus;
  createdAt: string;
  updatedAt?: string | null;
  totalAssignedCalls: number;
  completedCalls: number;
  activeCallId?: string | null;
  recentCalls: AgentCallItem[];
}

export interface AgentFilter {
  search?: string;
  status?: AgentStatus | '';
  team?: string;
  isActive?: boolean | '';
}
