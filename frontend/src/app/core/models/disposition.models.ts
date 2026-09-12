export interface CallDisposition {
  id: string;
  name: string;
  code: string;
  description?: string | null;
  requiresFollowUp: boolean;
  requiresNotes: boolean;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateDispositionRequest {
  name: string;
  code: string;
  description?: string | null;
  requiresFollowUp?: boolean;
  requiresNotes?: boolean;
  sortOrder?: number;
}

export interface UpdateDispositionRequest {
  name: string;
  description?: string | null;
  requiresFollowUp: boolean;
  requiresNotes: boolean;
  sortOrder: number;
  isActive: boolean;
}

export interface CompleteCallRequest {
  dispositionId: string;
  notes?: string | null;
  followUpAt?: string | null;
  followUpNotes?: string | null;
}
