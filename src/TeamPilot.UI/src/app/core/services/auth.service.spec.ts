import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { UserDto } from '../models';

function userWithRoles(roles: UserDto['roles']): UserDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Test User',
    email: 'test@example.com',
    roles,
    status: 'Active',
    createdAtUtc: new Date().toISOString(),
  };
}

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
  });

  it('isAuthenticated_NoSessionRestored_ReturnsFalse', () => {
    expect(service.isAuthenticated()).toBe(false);
  });

  it('canApprove_UserIsAnalystOnly_ReturnsFalse', () => {
    service['currentUserSignal'].set(userWithRoles(['Analyst']));
    expect(service.canApprove()).toBe(false);
  });

  it('canApprove_UserIsDeveloper_ReturnsTrue', () => {
    service['currentUserSignal'].set(userWithRoles(['Developer']));
    expect(service.canApprove()).toBe(true);
  });

  it('isAdmin_UserHasAdminRole_ReturnsTrue', () => {
    service['currentUserSignal'].set(userWithRoles(['Admin']));
    expect(service.isAdmin()).toBe(true);
  });

  it('clearSession_AfterLogin_ResetsUserAndToken', () => {
    service['currentUserSignal'].set(userWithRoles(['Admin']));
    service['accessTokenSignal'].set('a-token');

    service.clearSession();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken).toBeNull();
  });
});
