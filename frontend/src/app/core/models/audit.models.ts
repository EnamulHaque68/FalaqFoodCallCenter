export interface AuditLogItem {
  id: string;
  userId?: string | null;
  userName?: string | null;
  userRole?: string | null;
  action: string;
  entityName: string;
  entityId?: string | null;
  detailsJson?: string | null;
  createdAt: string;
}

export interface AuditLogPagedResult {
  items: AuditLogItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AuditLogFilter {
  page?: number;
  pageSize?: number;
  search?: string;
  action?: string;
  entityName?: string;
  userId?: string;
  fromDate?: string;
  toDate?: string;
}
