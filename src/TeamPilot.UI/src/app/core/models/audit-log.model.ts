import { AuditEventType } from './enums';

export interface AuditLogEntryDto {
  id: string;
  userId: string | null;
  eventType: AuditEventType;
  detail: string | null;
  ipAddress: string | null;
  createdAtUtc: string;
}
