import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IncomingCallEvent } from '../models/incoming-call.models';

export interface CallAssignedEvent {
  callId: string;
  agentId: string;
  agentName?: string | null;
  customerId?: string | null;
  customerName?: string | null;
  phoneNumber: string;
  direction: string;
  status: string;
  occurredAtUtc: string;
}

export interface NotificationEvent {
  id: string;
  title: string;
  message: string;
  severity: 'Info' | 'Warning' | 'Error' | 'Success' | string;
  targetType?: string | null;
  targetId?: string | null;
  occurredAtUtc: string;
}

export interface RealtimeRefreshEvent {
  type: 'agent-status' | 'call-status' | 'queue-updated' | 'incoming-call' | 'call-transferred' | 'call-assigned' | 'notification';
  payload: unknown;
}

@Injectable({ providedIn: 'root' })
export class CallCenterRealtimeService {
  private readonly eventsSubject = new Subject<RealtimeRefreshEvent>();
  readonly events$ = this.eventsSubject.asObservable();

  private readonly incomingSubject = new BehaviorSubject<IncomingCallEvent | null>(null);
  readonly incomingCalls$ = this.incomingSubject.asObservable();

  private readonly assignedSubject = new Subject<CallAssignedEvent>();
  readonly callAssigned$ = this.assignedSubject.asObservable();

  private readonly notificationSubject = new Subject<NotificationEvent>();
  readonly notifications$ = this.notificationSubject.asObservable();

  clearIncomingCall(): void { this.incomingSubject.next(null); }

  private connection?: HubConnection;
  private started = false;

  connect(): void {
    if (this.started) return;

    this.connection = new HubConnectionBuilder()
      .withUrl(environment.signalRHubUrl, {
        accessTokenFactory: () => sessionStorage.getItem('falaq_access_token') ?? ''
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('AgentStatusChanged', payload =>
      this.eventsSubject.next({ type: 'agent-status', payload }));

    this.connection.on('CallStatusChanged', payload =>
      this.eventsSubject.next({ type: 'call-status', payload }));

    this.connection.on('QueueUpdated', payload =>
      this.eventsSubject.next({ type: 'queue-updated', payload }));

    this.connection.on('IncomingCall', payload => {
      this.eventsSubject.next({ type: 'incoming-call', payload });
      this.incomingSubject.next(payload as IncomingCallEvent);
    });

    this.connection.on('CallTransferred', payload =>
      this.eventsSubject.next({ type: 'call-transferred', payload }));

    this.connection.on('CallAssigned', payload => {
      this.eventsSubject.next({ type: 'call-assigned', payload });
      this.assignedSubject.next(payload as CallAssignedEvent);
    });

    this.connection.on('Notification', payload => {
      this.eventsSubject.next({ type: 'notification', payload });
      this.notificationSubject.next(payload as NotificationEvent);
    });

    this.started = true;
    void this.start();
  }

  async stop(): Promise<void> {
    if (!this.connection) return;
    await this.connection.stop();
    this.started = false;
  }

  async joinQueue(queueId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('JoinQueue', queueId);
    }
  }

  async leaveQueue(queueId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('LeaveQueue', queueId);
    }
  }

  async joinCall(callId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('JoinCall', callId);
    }
  }

  async leaveCall(callId: string): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      await this.connection.invoke('LeaveCall', callId);
    }
  }

  private async start(): Promise<void> {
    if (!this.connection || this.connection.state !== HubConnectionState.Disconnected) return;
    try {
      await this.connection.start();
    } catch {
      this.started = false;
      setTimeout(() => this.connect(), 5000);
    }
  }
}
