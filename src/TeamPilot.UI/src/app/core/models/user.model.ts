import { UserRole, UserStatus } from './enums';

export interface UserDto {
  id: string;
  name: string;
  email: string;
  roles: UserRole[];
  status: UserStatus;
  createdAtUtc: string;
}

export interface SetUserRolesRequest {
  roles: UserRole[];
}

export interface SetUserStatusRequest {
  status: UserStatus;
}

export interface SetUserProjectsRequest {
  projectIds: string[];
}
