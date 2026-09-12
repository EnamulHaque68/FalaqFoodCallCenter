import { CallDirection, CallStatus } from './agent-dashboard.models';

export interface CallQueue {
  id: string;
  name: string;
  priority: number;
  isActive: boolean;
  waitingCallsCount: number;
  averageWaitSeconds: number;
  longestWaitSeconds: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface QueueSummary {
  totalQueues: number;
  activeQueues: number;
  totalWaitingCalls: number;
  averageWaitSeconds: number;
  longestWaitSeconds: number;
  availableAgentsCount: number;
}

export interface CallQueueEntry {
  id: string;
  callQueueId: string;
  queueName: string;
  callId: string;
  position: number;
  priority: number;
  enqueuedAt: string;
  waitSeconds: number;
  phoneNumber: string;
  customerId?: string | null;
  customerName?: string | null;
  direction: CallDirection;
  status: CallStatus;
}

export interface CreateQueueRequest {
  name: string;
  priority: number;
  isActive: boolean;
}

export interface UpdateQueueRequest {
  name: string;
  priority: number;
  isActive: boolean;
}
