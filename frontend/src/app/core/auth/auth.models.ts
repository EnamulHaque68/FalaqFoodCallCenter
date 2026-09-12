export type UserRole = 'Admin' | 'Supervisor' | 'Agent' | string;

export interface LoginRequest {
  username: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  userName: string;
  role: string;
  permissions?: string[];
}

export interface AuthUser {
  id: string;
  username: string;
  role: string;
  permissions: string[];
}
