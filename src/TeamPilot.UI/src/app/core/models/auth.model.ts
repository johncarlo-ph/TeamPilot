import { UserRole } from './enums';
import { UserDto } from './user.model';

export interface ExternalLoginRequest {
  idToken: string;
}

export type AuthProvider = 'google' | 'microsoft';

export interface LoginResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  user: UserDto;
}

export interface CurrentUserResponse {
  userId: string;
  name: string | null;
  email: string | null;
  roles: UserRole[];
}
