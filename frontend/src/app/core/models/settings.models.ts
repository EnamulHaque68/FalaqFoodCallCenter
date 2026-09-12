export interface GeneralSettings { callCenterName: string; operatingHours: string; supportEmail: string; defaultLocale: string; timezone: string; }
export interface CallSettings { ringTimeoutSeconds: number; autoAssignmentEnabled: boolean; recordingEnabled: boolean; allowCallTransfer: boolean; requireDisposition: boolean; maxCallDurationMinutes: number; }
export interface QueueSettings { maxQueueWaitSeconds: number; maxQueueCapacity: number; queueTimeoutAction: string; priorityRoutingEnabled: boolean; announceQueuePosition: boolean; }
export interface NotificationSettings { soundAlertsEnabled: boolean; queueWaitAlertThresholdSeconds: number; missedCallAlertsEnabled: boolean; desktopNotificationsEnabled: boolean; }
export interface SecuritySettings { sessionTimeoutMinutes: number; maxLoginAttempts: number; requireStrongPassword: boolean; auditLoggingEnabled: boolean; restrictAgentOutbound: boolean; }
export interface SettingItem { id: string; key: string; value: string; category: string; dataType: string; description?: string | null; updatedAt: string; }
export interface SystemSettings { general: GeneralSettings; call: CallSettings; queue: QueueSettings; notification: NotificationSettings; security: SecuritySettings; rawSettings: SettingItem[]; }
