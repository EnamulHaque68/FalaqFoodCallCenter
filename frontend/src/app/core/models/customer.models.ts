export interface Customer {
  id: string;
  fullName: string;
  phone: string;
  email?: string | null;
  address?: string | null;
  crmCustomerId?: string | null;
  notes?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateCustomerRequest {
  fullName: string;
  phone: string;
  email?: string | null;
  address?: string | null;
  crmCustomerId?: string | null;
  notes?: string | null;
}

export interface UpdateCustomerRequest {
  fullName: string;
  phone: string;
  email?: string | null;
  address?: string | null;
  crmCustomerId?: string | null;
  notes?: string | null;
}

export interface CustomerPagedResult {
  items: Customer[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CustomerCallItem {
  id: string;
  direction: string | number;
  status: string | number;
  startedAt: string;
  endedAt?: string | null;
  durationSeconds?: number | null;
  callDispositionId?: string | null;
  phoneNumber: string;
}

export interface CustomerCallsPagedResult {
  items: CustomerCallItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
