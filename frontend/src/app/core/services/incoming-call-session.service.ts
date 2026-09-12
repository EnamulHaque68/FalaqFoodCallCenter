import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { CustomerSummary, IncomingCallEvent } from '../models/incoming-call.models';

export interface ActiveCallSession {
  event: IncomingCallEvent;
  customer: CustomerSummary | null;
}

@Injectable({ providedIn: 'root' })
export class IncomingCallSessionService {
  private readonly subject = new BehaviorSubject<ActiveCallSession | null>(null);
  readonly session$ = this.subject.asObservable();

  setSession(session: ActiveCallSession): void { this.subject.next(session); }
  clear(): void { this.subject.next(null); }
  get snapshot(): ActiveCallSession | null { return this.subject.value; }
}
