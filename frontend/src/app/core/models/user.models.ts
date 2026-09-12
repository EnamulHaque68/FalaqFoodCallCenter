export interface UserListItem {
  id: string;
  userName: string;
  roleName: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
  lastLoginAt?: string | null;
  agentId?: string | null;
  agentName?: string | null;
  employeeCode?: string | null;
  team?: string | null;
  agentStatus?: string | null;
}

export interface CreateUserRequest {
  userName: string;
  password: string;
  roleName: string;
  displayName?: string;
  employeeCode?: string;
  team?: string;
}

export interface UpdateUserRequest {
  userName: string;
  roleName: string;
  isActive: boolean;
  displayName?: string;
  team?: string;
}

export interface ResetPasswordRequest {
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface RoleItem {
  id: string;
  name: string;
  description?: string;
  permissions: string[];
}

export interface UserFilter {
  search?: string;
  role?: string;
  isActive?: boolean | string;
}
