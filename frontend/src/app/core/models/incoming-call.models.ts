import { CallDirection, CallStatus } from './agent-dashboard.models';

export interface IncomingCallEvent {
  callId: string;
  customerId?: string | null;
  phoneNumber: string;
  direction: string;
  status: string;
  occurredAtUtc: string;
}

export interface CustomerSummary {
  id: string;
  fullName: string;
  phone: string;
  email?: string | null;
  address?: string | null;
}

export interface TelephonyCallResponse {
  callId: string;
  providerCallId: string;
  direction: CallDirection;
  status: CallStatus;
  phoneNumber: string;
  correlationId: string;
  customerId?: string | null;
  assignedAgentId?: string | null;
}

export enum TransferType {
  Blind = 1,
  Warm = 2,
  Supervisor = 3
}

export interface EligibleAgent {
  agentId: string;
  displayName: string;
  employeeCode: string;
  team: string;
  status: string;
  roleName: string;
  isEligible: boolean;
}

export interface CallTransferredEvent {
  callId: string;
  fromAgentId?: string | null;
  toAgentId?: string | null;
  targetQueueId?: string | null;
  transferType: string;
  reason?: string | null;
  occurredAtUtc: string;
}

export interface TransferCallRequest {
  targetAgentId?: string | null;
  targetQueueId?: string | null;
  transferType?: TransferType;
  reason?: string | null;
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

export interface TelephonyTokenResponse {
  token: string;
  identity: string;
  provider: string;
  voiceNumber?: string | null;
  expiresInSeconds: number;
}

