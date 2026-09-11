import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthProvider, CurrentUserResponse, ExternalLoginRequest, LoginResponse } from '../models';
import { UserDto, UserRole } from '../models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/auth`;

  /** Kept in memory only — never persisted to storage. Restored on bootstrap via the refresh cookie. */
  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly currentUserSignal = signal<UserDto | null>(null);

  readonly currentUser = this.currentUserSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);

  get accessToken(): string | null {
    return this.accessTokenSignal();
  }

  hasRole(role: UserRole): boolean {
    return this.currentUserSignal()?.roles.includes(role) ?? false;
  }

  isAdmin(): boolean {
    return this.hasRole('Admin');
  }

  /** Admins and Developers may Approve a review; Analysts may not (mirrors ApprovalGateService). */
  canApprove(): boolean {
    return this.hasRole('Admin') || this.hasRole('Developer');
  }

  loginWithIdToken(provider: AuthProvider, idToken: string): Observable<LoginResponse> {
    const request: ExternalLoginRequest = { idToken };
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/login/${provider}`, request, { withCredentials: true })
      .pipe(tap((response) => this.applySession(response)));
  }

  /** Attempts to silently restore a session from the HttpOnly refresh cookie on app bootstrap. */
  tryRestoreSession(): Observable<boolean> {
    return this.refresh().pipe(
      map(() => true),
      catchError(() => of(false))
    );
  }

  refresh(): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/refresh`, null, { withCredentials: true })
      .pipe(tap((response) => this.applySession(response)));
  }

  fetchCurrentUser(): Observable<CurrentUserResponse> {
    return this.http.get<CurrentUserResponse>(`${this.baseUrl}/me`);
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/logout`, null, { withCredentials: true }).pipe(
      tap(() => this.clearSession())
    );
  }

  clearSession(): void {
    this.accessTokenSignal.set(null);
    this.currentUserSignal.set(null);
  }

  private applySession(response: LoginResponse): void {
    this.accessTokenSignal.set(response.accessToken);
    this.currentUserSignal.set(response.user);
  }
}
